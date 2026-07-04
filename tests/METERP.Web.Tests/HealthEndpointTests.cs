using System.Net;
using System.Text.Json;
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
    public async Task Health_Liveness_ReturnsHealthyStatusJson()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Healthy", doc.RootElement.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WithStructuredJson()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("database", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ai", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_IncludesAiConfigurationProbe()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.True(entries.TryGetProperty("ai", out var aiEntry));
        Assert.Equal("Healthy", aiEntry.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_IncludesTotalDuration()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("totalDuration", out var duration));
        Assert.False(string.IsNullOrWhiteSpace(duration.GetString()));
    }

    [Fact]
    public async Task HealthReady_IncludesDatabaseProbe()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        Assert.True(entries.TryGetProperty("database", out var databaseEntry));
        Assert.Equal("Healthy", databaseEntry.GetProperty("status").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_EntryProbes_IncludeDuration()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var entries = doc.RootElement.GetProperty("entries");
        foreach (var entry in entries.EnumerateObject())
        {
            Assert.True(entry.Value.TryGetProperty("duration", out var duration), $"Entry '{entry.Name}' missing duration.");
            Assert.False(string.IsNullOrWhiteSpace(duration.GetString()));
        }
    }

    [Fact]
    public async Task Health_Liveness_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task HealthReady_IsNotRateLimited_UnderBurst()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}