using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Integrations;

public class BillingWebhookService : IBillingWebhookService
{
    private readonly AppDbContext _dbContext;
    private readonly BillingOptions _options;
    private readonly ILogger<BillingWebhookService> _logger;

    public BillingWebhookService(
        AppDbContext dbContext,
        IOptions<BillingOptions> options,
        ILogger<BillingWebhookService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BillingWebhookResult> ProcessStripeEventAsync(
        string rawBody,
        string? stripeSignatureHeader,
        bool allowUnsignedForDev,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return BillingWebhookResult.InvalidPayload("empty_body");

        if (_options.IsSignatureRequired)
        {
            if (!StripeWebhookSignatureValidator.IsValid(rawBody, stripeSignatureHeader, _options.WebhookSecret))
                return BillingWebhookResult.InvalidSignature();
        }
        else if (!allowUnsignedForDev)
        {
            return BillingWebhookResult.InvalidSignature();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Billing webhook JSON parse failed");
            return BillingWebhookResult.InvalidPayload("json_parse_failed");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
                return BillingWebhookResult.InvalidPayload("missing_type");

            var eventType = typeProp.GetString() ?? string.Empty;
            if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var obj))
                return BillingWebhookResult.InvalidPayload("missing_data_object");

            return eventType switch
            {
                "customer.subscription.updated" => await HandleSubscriptionUpdatedAsync(obj, ct),
                "customer.subscription.deleted" => await HandleSubscriptionDeletedAsync(obj, ct),
                "checkout.session.completed" => await HandleCheckoutCompletedAsync(obj, ct),
                _ => new BillingWebhookResult(BillingWebhookOutcome.Ignored, eventType)
            };
        }
    }

    private async Task<BillingWebhookResult> HandleSubscriptionUpdatedAsync(JsonElement subscription, CancellationToken ct)
    {
        var tenant = await ResolveTenantAsync(subscription, ct);
        if (tenant == null)
            return new BillingWebhookResult(BillingWebhookOutcome.Ignored, "tenant_not_found");

        var status = subscription.TryGetProperty("status", out var statusProp)
            ? statusProp.GetString() ?? "unknown"
            : "unknown";

        tenant.SubscriptionStatus = status;
        tenant.StripeCustomerId ??= GetStringProperty(subscription, "customer");

        if (status is "active" or "trialing")
        {
            var tier = ParseTierFromMetadata(subscription) ?? tenant.Tier;
            TenantQuotaDefaults.ApplyBillingTier(tenant, tier);
            if (tier == SubscriptionTier.Enterprise)
                tenant.EnabledFeatures = TenantQuotaDefaults.GetDefaultFeatures(tier) + ",compliance";
            tenant.IsActive = true;
        }
        else if (status is "past_due" or "unpaid")
        {
            tenant.IsActive = true;
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Billing webhook updated tenant {Subdomain} tier {Tier} status {Status}",
            tenant.Subdomain, tenant.Tier, tenant.SubscriptionStatus);

        return new BillingWebhookResult(BillingWebhookOutcome.TierUpdated, tenant.Subdomain);
    }

    private async Task<BillingWebhookResult> HandleSubscriptionDeletedAsync(JsonElement subscription, CancellationToken ct)
    {
        var tenant = await ResolveTenantAsync(subscription, ct);
        if (tenant == null)
            return new BillingWebhookResult(BillingWebhookOutcome.Ignored, "tenant_not_found");

        tenant.SubscriptionStatus = "canceled";
        TenantQuotaDefaults.ApplyBillingTier(tenant, SubscriptionTier.Starter);
        tenant.IsActive = true;

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("Billing webhook canceled subscription for tenant {Subdomain}", tenant.Subdomain);

        return new BillingWebhookResult(BillingWebhookOutcome.SubscriptionCanceled, tenant.Subdomain);
    }

    private async Task<BillingWebhookResult> HandleCheckoutCompletedAsync(JsonElement session, CancellationToken ct)
    {
        var customerId = GetStringProperty(session, "customer");
        var subdomain = GetMetadataString(session, "tenant_subdomain");
        if (string.IsNullOrWhiteSpace(subdomain))
            return new BillingWebhookResult(BillingWebhookOutcome.Ignored, "missing_tenant_subdomain");

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain.Trim().ToLowerInvariant() && !t.IsDeleted, ct);

        if (tenant == null)
            return new BillingWebhookResult(BillingWebhookOutcome.Ignored, "tenant_not_found");

        if (!string.IsNullOrWhiteSpace(customerId))
            tenant.StripeCustomerId = customerId;

        await _dbContext.SaveChangesAsync(ct);
        return new BillingWebhookResult(BillingWebhookOutcome.CustomerLinked, tenant.Subdomain);
    }

    private async Task<Tenant?> ResolveTenantAsync(JsonElement subscription, CancellationToken ct)
    {
        var subdomain = GetMetadataString(subscription, "tenant_subdomain");
        if (!string.IsNullOrWhiteSpace(subdomain))
        {
            var bySubdomain = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Subdomain == subdomain.Trim().ToLowerInvariant() && !t.IsDeleted, ct);
            if (bySubdomain != null)
                return bySubdomain;
        }

        var customerId = GetStringProperty(subscription, "customer");
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            return await _dbContext.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.StripeCustomerId == customerId && !t.IsDeleted, ct);
        }

        return null;
    }

    private static SubscriptionTier? ParseTierFromMetadata(JsonElement subscription)
    {
        var tierText = GetMetadataString(subscription, "tier");
        if (string.IsNullOrWhiteSpace(tierText))
            return null;

        return tierText.Trim().ToLowerInvariant() switch
        {
            "demo" => SubscriptionTier.Demo,
            "starter" => SubscriptionTier.Starter,
            "professional" or "pro" => SubscriptionTier.Professional,
            "enterprise" => SubscriptionTier.Enterprise,
            _ => null
        };
    }

    private static string? GetMetadataString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty("metadata", out var metadata))
            return null;
        if (!metadata.TryGetProperty(key, out var value))
            return null;
        return value.GetString();
    }

    private static string? GetStringProperty(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) ? prop.GetString() : null;
}