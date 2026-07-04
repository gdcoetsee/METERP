using System.Net;
using METERP.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace METERP.Web.Tests;

public class LoginTwoFactorEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly MeterpWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoginTwoFactorEndpointTests(MeterpWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task LoginTwoFactor_WithoutToken_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/login-2fa");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginTwoFactor_WithInvalidToken_RedirectsToLogin()
    {
        var response = await _client.GetAsync("/login-2fa?token=invalid-challenge-token");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginTwoFactor_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/login-2fa?token=invalid-challenge-token");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task LoginTwoFactor_WithValidToken_ReturnsTwoFactorForm()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPendingTwoFactorChallengeStore>();
        var userId = Guid.NewGuid();
        var token = store.CreateChallenge(userId);

        var response = await _client.GetAsync($"/login-2fa?token={Uri.EscapeDataString(token)}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("login-2fa-code", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login-2fa-submit", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(userId, store.GetChallenge(token));
    }

    [Fact]
    public async Task LoginTwoFactor_WithExpiredToken_RedirectsToLogin()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPendingTwoFactorChallengeStore>();
        var token = store.CreateChallenge(Guid.NewGuid(), TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var response = await _client.GetAsync($"/login-2fa?token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}