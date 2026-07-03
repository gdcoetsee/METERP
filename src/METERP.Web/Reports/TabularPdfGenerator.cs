using METERP.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace METERP.Web.Reports;

/// <summary>
/// Simple tabular PDF export for list screens (invoices, jobs, reports).
/// </summary>
public static class TabularPdfGenerator
{
    static TabularPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(
        string title,
        string[] headers,
        IEnumerable<string[]> rows,
        TenantBranding? branding = null)
    {
        branding ??= TenantBranding.Default;
        var generatedAt = DateTime.UtcNow;
        var rowList = rows.ToList();
        var brandColor = branding.ColorHex;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(branding.DisplayName).SemiBold().FontSize(14).FontColor(brandColor);
                    col.Item().Text(title).FontSize(11);
                    col.Item().Text($"Generated: {generatedAt:yyyy-MM-dd HH:mm} UTC")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(brandColor);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (var i = 0; i < headers.Length; i++)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var h in headers)
                            header.Cell().Background(brandColor).Padding(4).Text(h).SemiBold().FontColor(Colors.White);
                    });

                    foreach (var row in rowList)
                    {
                        foreach (var cell in row)
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(cell ?? "");
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"{branding.DisplayName} — Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }
}