using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace METERP.Web.Tests;

public class LoginTwoFactorEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginTwoFactorEndpointTests(MeterpWebApplicationFactory factory)
    {
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
}