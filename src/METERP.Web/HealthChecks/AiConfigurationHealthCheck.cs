using METERP.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace METERP.Web.HealthChecks;

/// <summary>
/// Informational health probe for optional AI configuration (never fails readiness).
/// </summary>
public class AiConfigurationHealthCheck : IHealthCheck
{
    private readonly IAiConfigurationResolver _configResolver;

    public AiConfigurationHealthCheck(IAiConfigurationResolver configResolver)
    {
        _configResolver = configResolver;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.GetEffectiveAsync(cancellationToken);

        if (!config.Enabled)
            return HealthCheckResult.Healthy("AI disabled via configuration (Ai:Enabled=false).");

        if (!config.IsConfigured)
            return HealthCheckResult.Healthy("AI not configured (optional). Set Ai:ApiKey or tenant AI settings.");

        var source = config.FromTenantOverride ? "tenant override" : "deployment";
        return HealthCheckResult.Healthy($"AI configured via {source} ({config.ProviderName}, model: {config.Model}).");
    }
}