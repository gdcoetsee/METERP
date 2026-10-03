using System.Text;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Small quote-aware CSV reader for the one-way Access extracts. No ODBC and no third-party parser.
/// </summary>
public static class AccessCsvReader
{
    public static IReadOnlyList<Dictionary<string, string>> Read(string path, int? maxRows = null)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = ReadRecord(reader);
        if (header == null || header.Count == 0)
            return [];

        var rows = new List<Dictionary<string, string>>();
        while (ReadRecord(reader) is { } fields)
        {
            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                var key = header[i].Trim().TrimStart('\uFEFF');
                if (key.Length == 0)
                    continue;
                row[key] = i < fields.Count ? fields[i].Trim() : "";
            }

            if (row.Count == 0)
                continue;

            rows.Add(row);
            if (maxRows is > 0 && rows.Count >= maxRows.Value)
                break;
        }

        return rows;
    }

    public static string Field(IReadOnlyDictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static List<string>? ReadRecord(StreamReader reader)
    {
        if (reader.EndOfStream)
            return null;

        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var any = false;

        while (true)
        {
            var next = reader.Read();
            if (next == -1)
            {
                if (!any)
                    return null;
                fields.Add(current.ToString());
                return fields;
            }

            any = true;
            var ch = (char)next;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (ch == '\n')
            {
                fields.Add(current.ToString());
                return fields;
            }
            else if (ch != '\r')
            {
                current.Append(ch);
            }
        }
    }
}
