namespace METERP.Domain;

/// <summary>
/// Workshop job-card face. Reads Access tokens already stored on the job
/// (<c>TRFid=</c>, <c>JobCardNo=</c>, <c>Team=</c>, and the same keys with a colon
/// as written into the job description). No schema change and no writes.
/// </summary>
public sealed record JobCardFace(
    string TrfId,
    string JobCardNo,
    string Team,
    string CustomerOrderNo,
    string Region)
{
    public const string Blank = "—";

    public static JobCardFace From(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Read(job.Notes, job.Description, job.JobNumber);
    }

    /// <summary>
    /// Missing or empty tokens become <see cref="Blank"/>. Never throws on empty notes.
    /// TRFid falls back to the job number when the note has no TRFid token.
    /// </summary>
    public static JobCardFace Read(string? notes, string? description = null, string? jobNumber = null)
    {
        var trf = First(
            InvoiceLegacyRef.TryReadTrfId(notes),
            InvoiceLegacyRef.TryReadTrfId(description),
            ReadToken(notes, "TRFid"),
            ReadToken(description, "TRFid"),
            string.IsNullOrWhiteSpace(jobNumber) ? null : jobNumber.Trim());

        var cards = new List<string>();
        AddCards(cards, InvoiceLegacyRef.ReadJobCardNumbers(notes));
        AddCards(cards, InvoiceLegacyRef.ReadJobCardNumbers(description));
        AddCards(cards, SplitList(ReadToken(notes, "JobCardNo")));
        AddCards(cards, SplitList(ReadToken(description, "JobCardNo")));

        return new JobCardFace(
            Show(trf),
            cards.Count == 0 ? Blank : string.Join(", ", cards),
            Show(First(ReadToken(notes, "Team"), ReadToken(description, "Team"))),
            Show(First(ReadToken(notes, "CustomerOrderNo"), ReadToken(description, "CustomerOrderNo"))),
            Show(First(ReadToken(notes, "Region"), ReadToken(description, "Region"))));
    }

    private static string Show(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Blank : value.Trim();

    private static string? First(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static void AddCards(List<string> cards, IEnumerable<string>? values)
    {
        if (values == null)
            return;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var trimmed = value.Trim();
            if (cards.Exists(card => string.Equals(card, trimmed, StringComparison.OrdinalIgnoreCase)))
                continue;
            cards.Add(trimmed);
        }
    }

    private static IEnumerable<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
                yield return part;
        }
    }

    private static string? ReadToken(string? text, string key)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return ReadMarker(text, key + "=") ?? ReadMarker(text, key + ":");
    }

    private static string? ReadMarker(string text, string marker)
    {
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return null;

            if (found > 0 && char.IsLetterOrDigit(text[found - 1]))
            {
                index = found + marker.Length;
                continue;
            }

            var start = found + marker.Length;
            var end = start;
            while (end < text.Length && text[end] is not '|' and not '\r' and not '\n')
                end++;

            var value = text[start..end].Trim();
            if (value.Length > 0)
                return value;

            index = end;
        }

        return null;
    }
}
