using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace METERP.Web.HealthChecks;

/// <summary>
/// Informational health probe for optional AI configuration (never fails readiness).
/// </summary>
public class AiConfigurationHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public AiConfigurationHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var aiSection = _configuration.GetSection("Ai");
        var enabled = aiSection.GetValue("Enabled", true);
        var apiKey = aiSection["ApiKey"];

        if (!enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("AI disabled via configuration (Ai:Enabled=false)."));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Task.FromResult(HealthCheckResult.Healthy("AI not configured (optional). Set Ai:ApiKey to enable Copilot."));
        }

        var model = aiSection["Model"] ?? "gpt-4o-mini";
        return Task.FromResult(HealthCheckResult.Healthy($"AI configured (model: {model})."));
    }
}