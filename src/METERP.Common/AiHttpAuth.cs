using System.Net.Http.Headers;

namespace METERP.Common;

/// <summary>
/// Applies provider-specific auth headers for OpenAI-compatible chat/completions calls.
/// Google Gemini accepts Bearer; do not send multiple auth headers (causes 400).
/// </summary>
public static class AiHttpAuth
{
    public static void ApplyApiKey(HttpRequestMessage request, string provider, string apiKey)
    {
        var key = apiKey.Trim();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public static bool LooksLikeGoogleKey(string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey) && apiKey.Trim().StartsWith("AIza", StringComparison.Ordinal);
}