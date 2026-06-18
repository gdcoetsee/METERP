using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;

namespace METERP.Infrastructure.Services;

public class AiConfigurationResolver : IAiConfigurationResolver
{
    private readonly IConfiguration _configuration;
    private readonly ITenantProvider? _tenantProvider;
    private readonly ITenantService? _tenantService;
    private readonly IDataProtector? _protector;

    public AiConfigurationResolver(
        IConfiguration configuration,
        ITenantProvider? tenantProvider = null,
        ITenantService? tenantService = null,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        _configuration = configuration;
        _tenantProvider = tenantProvider;
        _tenantService = tenantService;
        _protector = dataProtectionProvider?.CreateProtector("METERP.TenantAiSettings");
    }

    public bool IsDeploymentConfigured
    {
        get
        {
            var deployment = ReadDeploymentConfig();
            return deployment.IsConfigured;
        }
    }

    public async Task<AiRuntimeConfiguration> GetEffectiveAsync(CancellationToken ct = default)
    {
        var deployment = ReadDeploymentConfig();

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (tenantId == Guid.Empty || _tenantService == null)
            return deployment;

        try
        {
            var tenant = await _tenantService.GetByIdAsync(tenantId, ct);
            if (tenant == null || !tenant.AiUseTenantKey)
                return deployment;

            var apiKey = DecryptKey(tenant.AiApiKeyEncrypted);
            if (string.IsNullOrWhiteSpace(apiKey))
                return deployment;

            var provider = string.IsNullOrWhiteSpace(tenant.AiProvider)
                ? AiProviderProfiles.Custom
                : tenant.AiProvider;
            var preset = AiProviderProfiles.GetPreset(provider);

            var baseUrl = string.IsNullOrWhiteSpace(tenant.AiBaseUrl)
                ? preset.BaseUrl
                : tenant.AiBaseUrl.TrimEnd('/');
            var model = string.IsNullOrWhiteSpace(tenant.AiModel)
                ? preset.Model
                : tenant.AiModel;

            return new AiRuntimeConfiguration(
                Enabled: true,
                ApiKey: apiKey,
                BaseUrl: string.IsNullOrWhiteSpace(baseUrl) ? deployment.BaseUrl : baseUrl,
                Model: string.IsNullOrWhiteSpace(model) ? deployment.Model : model,
                TimeoutSeconds: deployment.TimeoutSeconds,
                ProviderName: provider,
                FromTenantOverride: true);
        }
        catch
        {
            return deployment;
        }
    }

    private string? DecryptKey(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted) || _protector == null)
            return null;

        try
        {
            return _protector.Unprotect(encrypted);
        }
        catch
        {
            return null;
        }
    }

    private AiRuntimeConfiguration ReadDeploymentConfig()
    {
        var aiSection = _configuration.GetSection("Ai");
        var apiKey = aiSection["ApiKey"];
        var baseUrl = aiSection["BaseUrl"]?.TrimEnd('/') ?? "https://api.openai.com/v1";
        var model = aiSection["Model"] ?? "gpt-4o-mini";
        var timeoutSeconds = int.TryParse(aiSection["TimeoutSeconds"], out var t) ? t : 60;
        var enabled = !bool.TryParse(aiSection["Enabled"], out var e) || e;
        var provider = aiSection["Provider"] ?? AiProviderProfiles.OpenAi;

        return new AiRuntimeConfiguration(
            Enabled: enabled,
            ApiKey: apiKey,
            BaseUrl: baseUrl,
            Model: model,
            TimeoutSeconds: timeoutSeconds,
            ProviderName: provider,
            FromTenantOverride: false);
    }
}