using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace METERP.Web.Tests;

public class LoginEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginEndpointTests(MeterpWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Login_ReturnsPageWithCredentialsForm()
    {
        var response = await _client.GetAsync("/login");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("login-email", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login-password", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login-submit", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("login-ready", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/login");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }
}