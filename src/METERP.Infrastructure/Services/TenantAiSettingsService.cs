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
    private readonly IAiConfigurationResolver? _configResolver;

    public TenantAiSettingsService(
        ITenantService tenantService,
        ITenantProvider tenantProvider,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<TenantAiSettingsService> logger,
        IAiConfigurationResolver? configResolver = null)
    {
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _protector = dataProtectionProvider.CreateProtector("METERP.TenantAiSettings");
        _logger = logger;
        _configResolver = configResolver;
    }

    public async Task<TenantAiSettingsDto> GetCurrentTenantSettingsAsync(CancellationToken ct = default)
    {
        var tenant = await RequireTenantAsync(ct);
        var providerName = string.IsNullOrWhiteSpace(tenant.AiProvider)
            ? AiProviderProfiles.Grok
            : tenant.AiProvider;
        var preset = AiProviderProfiles.GetPreset(providerName);

        return new TenantAiSettingsDto(
            Provider: providerName,
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
        var deployment = _configResolver == null
            ? null
            : await _configResolver.GetEffectiveAsync(ct);

        string? effectiveKey;
        string keySource;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            effectiveKey = apiKey.Trim();
            keySource = "the key in the form";
        }
        else
        {
            var stored = DecryptKey(tenant.AiApiKeyEncrypted);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                effectiveKey = stored;
                keySource = "the saved tenant key";
            }
            else if (!string.IsNullOrWhiteSpace(deployment?.ApiKey))
            {
                effectiveKey = deployment.ApiKey;
                keySource = "the deployment key (user-secrets or environment)";
            }
            else
            {
                effectiveKey = null;
                keySource = "";
            }
        }

        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            if (!string.IsNullOrWhiteSpace(tenant.AiApiKeyEncrypted))
                return new AiConnectionTestResult(false,
                    "Stored API key could not be read (encryption keys may have changed). Re-enter your API key and click Save Settings, then test again.");
            return new AiConnectionTestResult(false,
                "API key is required. Paste a key above and Save, or set user-secrets Ai:ApiKey, environment Ai__ApiKey, or XAI_API_KEY. Then test again from /settings/ai.");
        }

        if (provider == AiProviderProfiles.GoogleGemini && !AiHttpAuth.LooksLikeGoogleKey(effectiveKey))
            return new AiConnectionTestResult(false,
                "Google Gemini requires an AI Studio key starting with AIza. Create one at aistudio.google.com/apikey.");

        var preset = AiProviderProfiles.GetPreset(provider);
        var resolvedBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? preset.BaseUrl : baseUrl.TrimEnd('/');
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? preset.Model : model;
        if (string.IsNullOrWhiteSpace(resolvedBaseUrl) && !string.IsNullOrWhiteSpace(deployment?.BaseUrl))
            resolvedBaseUrl = deployment.BaseUrl;
        if (string.IsNullOrWhiteSpace(resolvedModel) && !string.IsNullOrWhiteSpace(deployment?.Model))
            resolvedModel = deployment.Model;

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
                _logger.LogWarning(
                    "AI test failed for provider {Provider}: {Status} {Body}",
                    provider,
                    response.StatusCode,
                    AiLogRedaction.Sanitize(body));
                return new AiConnectionTestResult(false, FormatApiError(response.StatusCode, body, provider));
            }

            return new AiConnectionTestResult(true, $"Connected to {provider} ({resolvedModel}) using {keySource}.");
        }
        catch (OperationCanceledException)
        {
            return new AiConnectionTestResult(false,
                $"Timed out calling {provider}. Check the base URL and model on /settings/ai, then test again.");
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
        var hint = status switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                " The API key was rejected. Paste a new key on /settings/ai, or set user-secrets Ai:ApiKey, environment Ai__ApiKey, or XAI_API_KEY.",
            System.Net.HttpStatusCode.NotFound =>
                " The model or base URL was not found. Check both on /settings/ai.",
            System.Net.HttpStatusCode.TooManyRequests =>
                " The provider rate-limited this call. Wait a moment and retry.",
            System.Net.HttpStatusCode.BadRequest =>
                " Check that the API key, base URL, and model all match the selected provider on /settings/ai.",
            System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.GatewayTimeout =>
                " The provider timed out. Try again, or raise Ai:TimeoutSeconds.",
            _ => " See /settings/ai to confirm the key, base URL, and model."
        };
        var sentence = detail.Trim().TrimEnd('.');
        return $"AI API {(int)status} ({status}) from {provider}: {sentence}.{hint}";
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
                if (err.ValueKind == JsonValueKind.String)
                {
                    var text = err.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
                else if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                {
                    var text = msg.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }
        catch { }

        return body.Length > 200 ? body[..200] + "…" : body;
    }
}