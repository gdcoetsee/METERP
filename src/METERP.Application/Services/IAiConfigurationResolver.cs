namespace METERP.Application.Services;

public interface IAiConfigurationResolver
{
    bool IsDeploymentConfigured { get; }

    Task<AiRuntimeConfiguration> GetEffectiveAsync(CancellationToken ct = default);
}