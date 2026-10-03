namespace METERP.Domain;

/// <summary>
/// Reads the Access import TRFid stored on invoice notes. Does not create a job link.
/// </summary>
public static class InvoiceLegacyRef
{
    public static string? TryReadTrfId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        const string marker = "TRFid=";
        var idx = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var start = idx + marker.Length;
        var end = start;
        while (end < notes.Length)
        {
            var c = notes[end];
            if (c is '|' or '\r' or '\n' or ' ' or '\t')
                break;
            end++;
        }

        var value = notes[start..end].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Job card numbers from Access notes (<c>JobCardNo=</c> or <c>JCs=</c>).</summary>
    public static IReadOnlyList<string> ReadJobCardNumbers(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return Array.Empty<string>();

        var cards = new List<string>();
        ReadMarkedList(notes, "JobCardNo=", cards);
        ReadMarkedList(notes, "JCs=", cards);
        return cards;
    }

    private static void ReadMarkedList(string notes, string marker, List<string> cards)
    {
        var index = 0;
        while ((index = notes.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = index + marker.Length;
            var end = start;
            while (end < notes.Length && notes[end] is not '|' and not '\r' and not '\n')
                end++;

            foreach (var part in notes[start..end].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length > 0 && !cards.Contains(part, StringComparer.OrdinalIgnoreCase))
                    cards.Add(part);
            }

            index = end;
        }
    }
}
