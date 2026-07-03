using System.Globalization;
using System.Text;

namespace METERP.Common;

/// <summary>
/// Shared CSV/TSV builders for list and report exports.
/// </summary>
public static class TabularExportHelper
{
    public static string BuildCsv(string header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header);
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    public static string BuildExcelTsv(string header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header);
        foreach (var row in rows)
            sb.AppendLine(string.Join("\t", row));
        return sb.ToString();
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    public static string FormatDecimal(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}