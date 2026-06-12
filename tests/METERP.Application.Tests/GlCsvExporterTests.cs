using METERP.Common;
using Xunit;

namespace METERP.Application.Tests;

public class GlCsvExporterTests
{
    [Fact]
    public void BuildJournalLinesCsv_IncludesHeaderAndEscapedValues()
    {
        var lines = new[]
        {
            new GlJournalLineExport(
                new DateTime(2026, 6, 12),
                "JE-2026-ABC123",
                "INV-001",
                "4000",
                "Revenue, Contracting",
                "Revenue",
                0m,
                11500m,
                "Memo with \"quotes\"",
                "Demo entry")
        };

        var csv = GlCsvExporter.BuildJournalLinesCsv(lines);

        Assert.StartsWith(GlCsvExporter.Header, csv);
        Assert.Contains("2026-06-12", csv);
        Assert.Contains("\"Revenue, Contracting\"", csv);
        Assert.Contains("\"Memo with \"\"quotes\"\"\"", csv);
        Assert.Contains("11500.00", csv);
    }
}