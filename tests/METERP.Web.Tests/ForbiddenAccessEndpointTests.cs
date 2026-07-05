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

public class ForbiddenAccessEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;

    public ForbiddenAccessEndpointTests(MeterpWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Tenants_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-tenants@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/tenants");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenants-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiSettings_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-aisettings@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/settings/ai");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ai-settings-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tenants_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-tenants@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/tenants");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-audit@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/audit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approvals_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-approvals@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/approvals");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiSettings_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-aisettings@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/settings/ai");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finance_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-finance@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/finance");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Users_ShowsAccessDenied_WhenFieldUserOnly()
    {
        const string email = "forbidden-field-users@acme.demo";
        await EnsureFieldOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_ShowsAccessDenied_WhenAuthenticatedWithoutPermission()
    {
        const string email = "forbidden-audit@acme.demo";
        await EnsureTenantOnlyUserAsync(email);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        var response = await client.GetAsync("/audit");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access Denied", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureTenantOnlyUserAsync(string email)
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
                Name = "Forbidden Test Tenant",
                Subdomain = "forbidtest",
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
    }

    private async Task EnsureFieldOnlyUserAsync(string email)
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
                Name = "Field Forbidden Test Tenant",
                Subdomain = "fieldforbid",
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
        await userManager.AddClaimAsync(user, new Claim("Permission", Permissions.FieldView));
    }
}