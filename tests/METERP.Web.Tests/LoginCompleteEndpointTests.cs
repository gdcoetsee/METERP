using System.Net;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace METERP.Web.Tests;

public class LoginCompleteEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;

    public LoginCompleteEndpointTests(MeterpWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginComplete_WithoutCredentials_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/login-complete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginComplete_WithUnknownEmail_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/login-complete?email=unknown%40nowhere.demo");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginComplete_WithExpiredTwofaToken_RedirectsToLogin()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPendingTwoFactorChallengeStore>();
        var token = store.CreateChallenge(Guid.NewGuid(), TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/login-complete?twofa={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginComplete_WithValidEmail_RedirectsToHome()
    {
        var tenantId = Guid.NewGuid();
        const string email = "logincomplete@acme.demo";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = CreateUserManager(db);

            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Login Complete Test",
                Subdomain = "logintest",
                IsActive = true
            });
            await db.SaveChangesAsync();

            await userManager.CreateAsync(new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                TenantId = tenantId
            }, "TestPass123!");
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/login-complete?email={Uri.EscapeDataString(email)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.True(
            location == "/" || location.EndsWith("/", StringComparison.Ordinal),
            $"Expected redirect to home, got: {location}");
    }

    [Fact]
    public async Task LoginComplete_IsNotRateLimited_UnderBurst()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var i = 0; i < 35; i++)
        {
            var response = await client.GetAsync("/login-complete?email=burst%40test.demo");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private static UserManager<ApplicationUser> CreateUserManager(AppDbContext db)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var store = new UserStore<ApplicationUser, ApplicationRole, AppDbContext, Guid>(db);
        return new UserManager<ApplicationUser>(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
            new IPasswordValidator<ApplicationUser>[] { new PasswordValidator<ApplicationUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            new LoggerFactory().CreateLogger<UserManager<ApplicationUser>>());
    }
}