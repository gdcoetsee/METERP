using System.Text.RegularExpressions;

namespace METERP.Common;

/// <summary>
/// MET office TRF / job numbers mentioned in copilot questions (FT16010, SD393, PD0085).
/// </summary>
public static class OfficeReferenceTokens
{
    private static readonly Regex Pattern = new(
        @"\b((?:FT|SD|PD)\d{2,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (Match match in Pattern.Matches(text))
        {
            var token = match.Groups[1].Value.ToUpperInvariant();
            if (seen.Add(token))
                list.Add(token);
        }

        return list;
    }
}
