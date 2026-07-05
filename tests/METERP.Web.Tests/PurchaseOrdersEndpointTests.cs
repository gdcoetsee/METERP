using System.Net;
using System.Security.Claims;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace METERP.Web.Tests;

public class PurchaseOrdersEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PurchaseOrdersEndpointTests(MeterpWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task PurchaseOrders_RedirectsToLogin_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/purchase-orders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseOrders_ReturnsOk_WhenAuthenticated()
    {
        const string email = "purchaseorders@acme.demo";
        await EnsurePurchaseOrdersUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/purchase-orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("purchase-orders-ready", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("purchase-orders-export-csv", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseOrders_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/purchase-orders");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private async Task EnsurePurchaseOrdersUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(email) != null)
            return;

        var tenantId = db.Tenants.Select(t => t.Id).FirstOrDefault();
        if (tenantId == Guid.Empty)
        {
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "PO Test Tenant",
                Subdomain = "potest",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        await userManager.CreateAsync(user, "TestPass123!");
        await userManager.AddClaimAsync(user, new Claim("TenantId", tenantId.ToString()));
        await userManager.AddClaimAsync(user, new Claim("Permission", Permissions.PurchaseOrdersView));
    }
}