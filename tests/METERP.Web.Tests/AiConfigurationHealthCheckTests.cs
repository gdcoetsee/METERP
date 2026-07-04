using METERP.Application.Services;
using METERP.Web.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using Xunit;

namespace METERP.Web.Tests;

public class AiConfigurationHealthCheckTests
{
    private static AiConfigurationHealthCheck CreateCheck(AiRuntimeConfiguration config)
    {
        var resolver = new Mock<IAiConfigurationResolver>();
        resolver.Setup(r => r.GetEffectiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(config);
        return new AiConfigurationHealthCheck(resolver.Object);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenAiDisabled()
    {
        var check = CreateCheck(new AiRuntimeConfiguration(
            Enabled: false,
            ApiKey: null,
            BaseUrl: "https://api.openai.com/v1",
            Model: "gpt-4o-mini",
            TimeoutSeconds: 45,
            ProviderName: "OpenAI",
            FromTenantOverride: false));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenAiNotConfigured()
    {
        var check = CreateCheck(new AiRuntimeConfiguration(
            Enabled: true,
            ApiKey: null,
            BaseUrl: "https://api.openai.com/v1",
            Model: "gpt-4o-mini",
            TimeoutSeconds: 45,
            ProviderName: "OpenAI",
            FromTenantOverride: false));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("not configured", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WithDeploymentSource_WhenConfigured()
    {
        var check = CreateCheck(new AiRuntimeConfiguration(
            Enabled: true,
            ApiKey: "sk-test-key",
            BaseUrl: "https://api.openai.com/v1",
            Model: "gpt-4o-mini",
            TimeoutSeconds: 45,
            ProviderName: "OpenAI",
            FromTenantOverride: false));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("deployment", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenAI", result.Description);
        Assert.Contains("gpt-4o-mini", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WithTenantOverrideSource_WhenConfigured()
    {
        var check = CreateCheck(new AiRuntimeConfiguration(
            Enabled: true,
            ApiKey: "tenant-sk-key",
            BaseUrl: "https://api.groq.com/v1",
            Model: "llama-3.3-70b",
            TimeoutSeconds: 60,
            ProviderName: "Groq",
            FromTenantOverride: true));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("tenant override", result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Groq", result.Description);
    }
}