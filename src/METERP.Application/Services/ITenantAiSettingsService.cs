namespace METERP.Application.Services;

public sealed record TenantAiSettingsDto(
    string Provider,
    string BaseUrl,
    string Model,
    bool UseTenantKey,
    string? MaskedApiKey,
    bool HasStoredKey);

public sealed record AiConnectionTestResult(bool Success, string Message);

public interface ITenantAiSettingsService
{
    Task<TenantAiSettingsDto> GetCurrentTenantSettingsAsync(CancellationToken ct = default);

    Task SaveCurrentTenantSettingsAsync(
        string provider,
        string baseUrl,
        string model,
        bool useTenantKey,
        string? apiKey,
        CancellationToken ct = default);

    Task<AiConnectionTestResult> TestConnectionAsync(
        string provider,
        string baseUrl,
        string model,
        string? apiKey,
        CancellationToken ct = default);
}