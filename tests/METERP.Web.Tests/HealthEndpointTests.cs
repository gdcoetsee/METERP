using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace METERP.Web.Tests;

public class HealthEndpointTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(MeterpWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Health_Liveness_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WithStructuredJson()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ai", body, StringComparison.OrdinalIgnoreCase);
    }
}