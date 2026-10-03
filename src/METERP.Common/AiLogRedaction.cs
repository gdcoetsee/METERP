using System.Text.RegularExpressions;

namespace METERP.Common;

/// <summary>
/// Strips likely API-key fragments before anything is written to logs.
/// </summary>
public static partial class AiLogRedaction
{
    private static readonly Regex KeyPattern = new(
        @"(sk-|xai-|AIza|gsk_|Bearer\s+)[A-Za-z0-9_\-]{4,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return "";

        var trimmed = body.Length > 180 ? body[..180] + "…" : body;
        return KeyPattern.Replace(trimmed, "$1[redacted]");
    }
}
