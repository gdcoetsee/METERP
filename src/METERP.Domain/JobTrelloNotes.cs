using System.Text.RegularExpressions;

namespace METERP.Domain;

/// <summary>
/// One whiteboard card already written onto <see cref="Job.Notes"/> by the FY2026 Trello overlay.
/// </summary>
public sealed record JobTrelloMarker(string Board, string ListName, string? CardUrl)
{
    /// <summary>Office label for the two MET boards. Other board names stay as stored.</summary>
    public string BoardLabel
    {
        get
        {
            if (Board.Contains("Field Teams", StringComparison.OrdinalIgnoreCase))
                return "Field Teams";
            if (Board.Contains("Workshop", StringComparison.OrdinalIgnoreCase))
                return "Workshop";
            return Board;
        }
    }
}

/// <summary>
/// Reads Trello markers from job notes. No live Trello API.
/// Overlay line: <c>Trello[Board/List]: …</c>. Stub line: <c>Board=… | List=…</c>.
/// </summary>
public static class JobTrelloNotes
{
    private static readonly Regex OverlayMarker = new(
        @"Trello\[(?<board>[^\[\]/\r\n]+)/(?<list>[^\[\]\r\n]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StubMarker = new(
        @"Board=(?<board>[^|\r\n]+)\|\s*List=(?<list>[^|\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CardUrl = new(
        @"https://trello\.com/\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<JobTrelloMarker> Parse(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return Array.Empty<JobTrelloMarker>();

        var found = new List<JobTrelloMarker>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string board, string list, int at)
        {
            board = board.Trim();
            list = list.Trim().TrimEnd('.', ',', ';');
            if (board.Length == 0 || list.Length == 0)
                return;

            var key = board + "\n" + list;
            if (!seen.Add(key))
                return;

            found.Add(new JobTrelloMarker(board, list, UrlOnLine(notes, at)));
        }

        foreach (Match match in OverlayMarker.Matches(notes))
            Add(match.Groups["board"].Value, match.Groups["list"].Value, match.Index);

        foreach (Match match in StubMarker.Matches(notes))
            Add(match.Groups["board"].Value, match.Groups["list"].Value, match.Index);

        return found;
    }

    private static string? UrlOnLine(string notes, int index)
    {
        var lineStart = notes.LastIndexOf('\n', index);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var lineEnd = notes.IndexOf('\n', index);
        if (lineEnd < 0)
            lineEnd = notes.Length;

        var url = CardUrl.Match(notes, lineStart, lineEnd - lineStart);
        if (!url.Success)
            return null;

        return url.Value.TrimEnd('.', ',', ';', ')', ']', '>');
    }
}
