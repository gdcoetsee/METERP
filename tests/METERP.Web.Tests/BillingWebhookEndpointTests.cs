using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using Xunit;

namespace METERP.Web.Tests;

public class BillingWebhookEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;

    public BillingWebhookEndpointTests(MeterpWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task StripeWebhook_UnsignedInTesting_UpdatesTenantTier()
    {
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = "Webhook Test Co",
            Subdomain = "webhooktest",
            Tier = SubscriptionTier.Starter
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var payload = """
            {
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "customer": "cus_test",
                  "status": "active",
                  "metadata": {
                    "tenant_subdomain": "webhooktest",
                    "tier": "enterprise"
                  }
                }
              }
            }
            """;

        var response = await client.PostAsync(
            "/webhooks/stripe",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await verifyDb.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        Assert.Equal(SubscriptionTier.Enterprise, tenant.Tier);
        Assert.Equal("active", tenant.SubscriptionStatus);
    }
}