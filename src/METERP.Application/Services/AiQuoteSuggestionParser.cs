using System.Text.Json;

namespace METERP.Application.Services;

/// <summary>
/// Parses LLM JSON responses into structured quote suggestions (testable without HTTP).
/// </summary>
public static class AiQuoteSuggestionParser
{
    public static AiQuoteSuggestion? TryParse(string? jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
            return null;

        try
        {
            using var suggestionDoc = JsonDocument.Parse(jsonContent);

            var reasoning = suggestionDoc.RootElement.TryGetProperty("reasoning", out var r)
                ? r.GetString() ?? ""
                : "";

            var lines = new List<AiSuggestedLine>();

            if (suggestionDoc.RootElement.TryGetProperty("suggestedLines", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    lines.Add(new AiSuggestedLine(
                        item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        item.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1,
                        item.TryGetProperty("unit", out var u) ? u.GetString() ?? "ea" : "ea",
                        item.TryGetProperty("lineType", out var lt) ? lt.GetString() ?? "Other" : "Other",
                        item.TryGetProperty("unitPrice", out var up) ? up.GetDecimal() : 0,
                        item.TryGetProperty("suggestedInventorySku", out var sku) ? sku.GetString() : null,
                        item.TryGetProperty("notes", out var n) ? n.GetString() : null
                    ));
                }
            }

            return new AiQuoteSuggestion(reasoning, lines);
        }
        catch
        {
            return null;
        }
    }
}