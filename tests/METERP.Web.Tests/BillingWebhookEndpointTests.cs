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
              "id": "evt_endpoint_enterprise",
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

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enterprise", body, StringComparison.OrdinalIgnoreCase);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await verifyDb.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        Assert.Equal(SubscriptionTier.Enterprise, tenant.Tier);
        Assert.Equal("active", tenant.SubscriptionStatus);
    }

    [Fact]
    public async Task StripeWebhook_DuplicateEvent_ReturnsOkWithoutChangingTier()
    {
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            Name = "Duplicate Webhook Co",
            Subdomain = "dupwebhook",
            Tier = SubscriptionTier.Starter
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var payload = """
            {
              "id": "evt_endpoint_duplicate",
              "type": "customer.subscription.updated",
              "data": {
                "object": {
                  "customer": "cus_dup",
                  "status": "active",
                  "metadata": {
                    "tenant_subdomain": "dupwebhook",
                    "tier": "professional"
                  }
                }
              }
            }
            """;

        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var first = await client.PostAsync("/webhooks/stripe", content);
        var second = await client.PostAsync("/webhooks/stripe", new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Contains("Duplicate", secondBody, StringComparison.OrdinalIgnoreCase);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await verifyDb.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        Assert.Equal(SubscriptionTier.Professional, tenant.Tier);
    }

    [Fact]
    public async Task StripeWebhook_IsNotRateLimited()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var payload = """
            {
              "id": "evt_rate_limit_probe",
              "type": "unknown.event.type",
              "data": { "object": {} }
            }
            """;

        for (var i = 0; i < 35; i++)
        {
            var response = await client.PostAsync(
                "/webhooks/stripe",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}