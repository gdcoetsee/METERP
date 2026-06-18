using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;

namespace METERP.Infrastructure.Services;

public class TenantAiSettingsService : ITenantAiSettingsService
{
    private readonly ITenantService _tenantService;
    private readonly ITenantProvider _tenantProvider;
    private readonly IDataProtector _protector;
    private readonly ILogger<TenantAiSettingsService> _logger;

    public TenantAiSettingsService(
        ITenantService tenantService,
        ITenantProvider tenantProvider,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TenantAiSettingsService> logger)
    {
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _protector = dataProtectionProvider.CreateProtector("METERP.TenantAiSettings");
        _logger = logger;
    }

    public async Task<TenantAiSettingsDto> GetCurrentTenantSettingsAsync(CancellationToken ct = default)
    {
        var tenant = await RequireTenantAsync(ct);
        var preset = AiProviderProfiles.GetPreset(tenant.AiProvider ?? AiProviderProfiles.OpenAi);

        return new TenantAiSettingsDto(
            Provider: string.IsNullOrWhiteSpace(tenant.AiProvider) ? AiProviderProfiles.OpenAi : tenant.AiProvider,
            BaseUrl: string.IsNullOrWhiteSpace(tenant.AiBaseUrl) ? preset.BaseUrl : tenant.AiBaseUrl,
            Model: string.IsNullOrWhiteSpace(tenant.AiModel) ? preset.Model : tenant.AiModel,
            UseTenantKey: tenant.AiUseTenantKey,
            MaskedApiKey: MaskKey(DecryptKey(tenant.AiApiKeyEncrypted)),
            HasStoredKey: !string.IsNullOrWhiteSpace(tenant.AiApiKeyEncrypted));
    }

    public async Task SaveCurrentTenantSettingsAsync(
        string provider,
        string baseUrl,
        string model,
        bool useTenantKey,
        string? apiKey,
        CancellationToken ct = default)
    {
        var tenant = await RequireTenantAsync(ct);
        tenant.AiProvider = provider;
        tenant.AiBaseUrl = baseUrl?.TrimEnd('/');
        tenant.AiModel = model;
        tenant.AiUseTenantKey = useTenantKey;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            tenant.AiApiKeyEncrypted = _protector.Protect(apiKey.Trim());
            tenant.AiUseTenantKey = true;
        }

        await _tenantService.UpdateAsync(tenant, ct);
    }

    public async Task<AiConnectionTestResult> TestConnectionAsync(
        string provider,
        string baseUrl,
        string model,
        string? apiKey,
        CancellationToken ct = default)
    {
        var tenant = await RequireTenantAsync(ct);
        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey.Trim()
            : DecryptKey(tenant.AiApiKeyEncrypted);

        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            if (!string.IsNullOrWhiteSpace(tenant.AiApiKeyEncrypted))
                return new AiConnectionTestResult(false,
                    "Stored API key could not be read (encryption keys may have changed). Re-enter your API key and click Save Settings, then test again.");
            return new AiConnectionTestResult(false, "API key is required to test the connection.");
        }

        if (provider == AiProviderProfiles.GoogleGemini && !AiHttpAuth.LooksLikeGoogleKey(effectiveKey))
            return new AiConnectionTestResult(false,
                "Google Gemini requires an AI Studio key starting with AIza. Create one at aistudio.google.com/apikey.");

        var preset = AiProviderProfiles.GetPreset(provider);
        var resolvedBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? preset.BaseUrl : baseUrl.TrimEnd('/');
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? preset.Model : model;

        if (string.IsNullOrWhiteSpace(resolvedBaseUrl) || string.IsNullOrWhiteSpace(resolvedModel))
            return new AiConnectionTestResult(false, "Base URL and model are required.");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            var payload = JsonSerializer.Serialize(new
            {
                model = resolvedModel,
                messages = new[] { new { role = "user", content = "Reply with OK" } },
                max_tokens = 5,
                temperature = 0
            });

            var url = $"{resolvedBaseUrl.TrimEnd('/')}/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            AiHttpAuth.ApplyApiKey(request, provider, effectiveKey);
            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI test failed for provider {Provider}: {Status} {Body}", provider, response.StatusCode, body);
                return new AiConnectionTestResult(false, FormatApiError(response.StatusCode, body, provider));
            }

            return new AiConnectionTestResult(true, $"Connected to {provider} ({resolvedModel}).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI connection test failed for provider {Provider}", provider);
            return new AiConnectionTestResult(false, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<Domain.Tenant> RequireTenantAsync(CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("No tenant context.");

        var tenant = await _tenantService.GetByIdAsync(tenantId, ct);
        if (tenant == null)
            throw new InvalidOperationException("Tenant not found.");

        return tenant;
    }

    private string? DecryptKey(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
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

    private static string MaskKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length < 8
            ? "(not set)"
            : $"{key[..4]}••••{key[^4..]}";

    internal static string FormatApiError(System.Net.HttpStatusCode status, string body, string provider)
    {
        var detail = TryParseErrorMessage(body);
        var hint = status == System.Net.HttpStatusCode.BadRequest
            ? " Check that ApiKey, BaseUrl, and Model all match the selected provider."
            : string.Empty;
        return $"AI API {(int)status} ({status}) from {provider}: {detail}.{hint}";
    }

    private static string TryParseErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "no response body";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? body;
            }
        }
        catch { }

        return body.Length > 200 ? body[..200] + "…" : body;
    }
}