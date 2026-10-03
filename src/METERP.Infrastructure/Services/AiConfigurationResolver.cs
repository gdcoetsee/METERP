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

    private readonly Func<string, string?> _environmentReader;

    public AiConfigurationResolver(
        IConfiguration configuration,
        ITenantProvider? tenantProvider = null,
        ITenantService? tenantService = null,
        IDataProtectionProvider? dataProtectionProvider = null,
        Func<string, string?>? environmentReader = null)
    {
        _configuration = configuration;
        _tenantProvider = tenantProvider;
        _tenantService = tenantService;
        _protector = dataProtectionProvider?.CreateProtector("METERP.TenantAiSettings");
        _environmentReader = environmentReader ?? Environment.GetEnvironmentVariable;
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
                ? deployment.ProviderName
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
        var apiKey = FirstNonBlank(aiSection["ApiKey"], _environmentReader("Ai__ApiKey"), _environmentReader("XAI_API_KEY"));
        var baseUrl = FirstNonBlank(aiSection["BaseUrl"], _environmentReader("Ai__BaseUrl"))
            ?.TrimEnd('/')
            ?? AiProviderProfiles.DefaultBaseUrl;
        var model = FirstNonBlank(aiSection["Model"], _environmentReader("Ai__Model"))
            ?? AiProviderProfiles.DefaultModel;
        var timeoutSeconds = int.TryParse(aiSection["TimeoutSeconds"], out var t) ? t : 60;
        var enabled = !bool.TryParse(
            FirstNonBlank(aiSection["Enabled"], _environmentReader("Ai__Enabled")),
            out var e) || e;
        var provider = FirstNonBlank(aiSection["Provider"], _environmentReader("Ai__Provider"))
            ?? AiProviderProfiles.InferProvider(baseUrl);

        return new AiRuntimeConfiguration(
            Enabled: enabled,
            ApiKey: apiKey,
            BaseUrl: baseUrl,
            Model: model,
            TimeoutSeconds: timeoutSeconds,
            ProviderName: provider,
            FromTenantOverride: false);
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}