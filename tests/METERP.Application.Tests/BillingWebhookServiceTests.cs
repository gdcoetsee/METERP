using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Integrations;
using METERP.Infrastructure.Persistence;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class BillingWebhookServiceTests
{
    private const string WebhookSecret = "whsec_test_secret_for_meterp";

    private static (AppDbContext Db, BillingWebhookService Service) CreateService(
        Guid tenantId,
        string subdomain = "acme",
        string? webhookSecret = WebhookSecret)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = "Acme Contractors",
            Subdomain = subdomain,
            Tier = SubscriptionTier.Starter,
            EnabledFeatures = "usage-tracking"
        });
        db.SaveChanges();

        var service = new BillingWebhookService(
            db,
            Microsoft.Extensions.Options.Options.Create(new BillingOptions { WebhookSecret = webhookSecret ?? string.Empty }),
            NullLogger<BillingWebhookService>.Instance);

        return (db, service);
    }

    [Fact]
    public async Task SubscriptionUpdated_Active_UpgradesTenantToProfessional()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateService(tenantId);
        await using (db)
        {
            var payload = """
                {
                  "type": "customer.subscription.updated",
                  "data": {
                    "object": {
                      "customer": "cus_acme_001",
                      "status": "active",
                      "metadata": {
                        "tenant_subdomain": "acme",
                        "tier": "professional"
                      }
                    }
                  }
                }
                """;

            var signature = StripeWebhookSignatureValidator.BuildSignatureHeader(WebhookSecret, payload);
            var result = await service.ProcessStripeEventAsync(payload, signature, allowUnsignedForDev: false);

            Assert.Equal(BillingWebhookOutcome.TierUpdated, result.Outcome);

            var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
            Assert.Equal(SubscriptionTier.Professional, tenant.Tier);
            Assert.Equal("active", tenant.SubscriptionStatus);
            Assert.Equal("cus_acme_001", tenant.StripeCustomerId);
            Assert.True(tenant.HasFeature("ai"));
            Assert.Equal(500, tenant.MaxQuotesPerMonth);
        }
    }

    [Fact]
    public async Task SubscriptionDeleted_DowngradesTenantToStarter()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateService(tenantId);
        await using (db)
        {
            var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
            tenant.Tier = SubscriptionTier.Enterprise;
            tenant.EnabledFeatures = "ai,usage-tracking,advanced-reports,compliance";
            tenant.StripeCustomerId = "cus_acme_001";
            await db.SaveChangesAsync();

            var payload = """
                {
                  "type": "customer.subscription.deleted",
                  "data": {
                    "object": {
                      "customer": "cus_acme_001",
                      "status": "canceled",
                      "metadata": { "tenant_subdomain": "acme" }
                    }
                  }
                }
                """;

            var signature = StripeWebhookSignatureValidator.BuildSignatureHeader(WebhookSecret, payload);
            var result = await service.ProcessStripeEventAsync(payload, signature, allowUnsignedForDev: false);

            Assert.Equal(BillingWebhookOutcome.SubscriptionCanceled, result.Outcome);

            tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
            Assert.Equal(SubscriptionTier.Starter, tenant.Tier);
            Assert.Equal("canceled", tenant.SubscriptionStatus);
            Assert.False(tenant.HasFeature("ai"));
        }
    }

    [Fact]
    public async Task InvalidSignature_RejectedWhenSecretConfigured()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateService(tenantId);
        await using (db)
        {
            var payload = """{ "type": "customer.subscription.updated", "data": { "object": {} } }""";
            var result = await service.ProcessStripeEventAsync(payload, "t=1,v1=bad", allowUnsignedForDev: false);

            Assert.Equal(BillingWebhookOutcome.InvalidSignature, result.Outcome);
        }
    }

    [Fact]
    public async Task UnsignedPayload_AcceptedInDevWhenSecretNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = CreateService(tenantId, webhookSecret: string.Empty);
        await using (db)
        {
            var payload = """
                {
                  "type": "checkout.session.completed",
                  "data": {
                    "object": {
                      "customer": "cus_beta_99",
                      "metadata": { "tenant_subdomain": "acme" }
                    }
                  }
                }
                """;

            var result = await service.ProcessStripeEventAsync(payload, stripeSignatureHeader: null, allowUnsignedForDev: true);

            Assert.Equal(BillingWebhookOutcome.CustomerLinked, result.Outcome);

            var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
            Assert.Equal("cus_beta_99", tenant.StripeCustomerId);
        }
    }
}