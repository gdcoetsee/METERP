namespace METERP.Application.Services;

/// <summary>
/// Effective AI settings after merging deployment config and optional tenant override.
/// </summary>
public sealed record AiRuntimeConfiguration(
    bool Enabled,
    string? ApiKey,
    string BaseUrl,
    string Model,
    int TimeoutSeconds,
    string ProviderName,
    bool FromTenantOverride)
{
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);

    public string MaskedApiKey =>
        string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length < 8
            ? "(not set)"
            : $"{ApiKey[..4]}••••{ApiKey[^4..]}";
}