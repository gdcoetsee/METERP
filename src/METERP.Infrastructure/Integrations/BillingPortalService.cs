using Microsoft.Extensions.Options;
using METERP.Application.Integrations;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Integrations;

public class BillingPortalService : IBillingPortalService
{
    private readonly BillingOptions _options;
    private readonly IStripeCustomerPortalClient _portalClient;

    public BillingPortalService(
        IOptions<BillingOptions> options,
        IStripeCustomerPortalClient portalClient)
    {
        _options = options.Value;
        _portalClient = portalClient;
    }

    public bool IsConfigured => _options.IsPortalConfigured;

    public string? GetCustomerPortalUrl(Tenant tenant)
    {
        if (!_options.IsPortalConfigured || string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            return null;

        if (string.IsNullOrWhiteSpace(_options.CustomerPortalBaseUrl))
            return null;

        var customerId = tenant.StripeCustomerId.Trim();
        if (string.IsNullOrEmpty(customerId))
            return null;

        var baseUrl = _options.CustomerPortalBaseUrl.Trim().TrimEnd('/');
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{separator}customer={Uri.EscapeDataString(customerId)}";
    }

    public async Task<string?> ResolveCustomerPortalUrlAsync(Tenant tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            return null;

        if (_options.CanCreateApiSessions)
        {
            var sessionUrl = await _portalClient.CreateSessionUrlAsync(
                tenant.StripeCustomerId,
                _options.CustomerPortalReturnUrl,
                ct);

            if (!string.IsNullOrWhiteSpace(sessionUrl))
                return sessionUrl;
        }

        return GetCustomerPortalUrl(tenant);
    }
}