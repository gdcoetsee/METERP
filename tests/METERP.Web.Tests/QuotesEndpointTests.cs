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

public class QuotesEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public QuotesEndpointTests(MeterpWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Quotes_RedirectsToLogin_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/quotes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quotes_ReturnsOk_WhenAuthenticated()
    {
        const string email = "quotes@acme.demo";
        await EnsureQuotesUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/quotes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("quotes-ready", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quotes-export-csv", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quotes_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/quotes");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private async Task EnsureQuotesUserAsync(string email)
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
                Name = "Quotes Test Tenant",
                Subdomain = "quotestest",
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
        await userManager.AddClaimAsync(user, new Claim("Permission", Permissions.QuotesView));
    }
}