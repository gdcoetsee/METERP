using System.Globalization;
using System.Text;

namespace METERP.Common;

/// <summary>
/// Pure CSV builder for GL journal exports (Xero/Sage-compatible column layout).
/// </summary>
public static class GlCsvExporter
{
    public const string Header =
        "EntryDate,EntryNumber,Reference,AccountCode,AccountName,AccountType,Debit,Credit,Memo,JournalDescription";

    public static string BuildJournalLinesCsv(IEnumerable<GlJournalLineExport> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);

        foreach (var line in lines)
        {
            sb.Append(Escape(line.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            sb.Append(',');
            sb.Append(Escape(line.EntryNumber));
            sb.Append(',');
            sb.Append(Escape(line.Reference));
            sb.Append(',');
            sb.Append(Escape(line.AccountCode));
            sb.Append(',');
            sb.Append(Escape(line.AccountName));
            sb.Append(',');
            sb.Append(Escape(line.AccountType));
            sb.Append(',');
            sb.Append(line.Debit.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(line.Credit.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Escape(line.Memo));
            sb.Append(',');
            sb.Append(Escape(line.JournalDescription));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}

public record GlJournalLineExport(
    DateTime EntryDate,
    string EntryNumber,
    string? Reference,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Debit,
    decimal Credit,
    string? Memo,
    string? JournalDescription);