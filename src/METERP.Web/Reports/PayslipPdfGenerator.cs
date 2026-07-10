using METERP.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace METERP.Web.Reports;

public static class PayslipPdfGenerator
{
    static PayslipPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(
        string employeeName,
        string periodLabel,
        decimal hours,
        decimal grossPay,
        decimal deductions,
        int laborEntryCount,
        TenantBranding? branding = null,
        string? employeeNumber = null,
        string? jobTitle = null)
    {
        branding ??= TenantBranding.Default;
        var net = Math.Max(0m, grossPay - deductions);
        var generatedAt = DateTime.UtcNow;
        var brandColor = branding.ColorHex;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{branding.DisplayName} — Payslip").SemiBold().FontSize(16).FontColor(brandColor);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(brandColor);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(employeeNumber))
                        col.Item().Text($"Employee #: {employeeNumber}");
                    col.Item().Text($"Employee: {employeeName}").SemiBold();
                    if (!string.IsNullOrWhiteSpace(jobTitle))
                        col.Item().Text($"Title: {jobTitle}");
                    col.Item().Text($"Period: {periodLabel}");
                    col.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                        });

                        void Row(string label, string value)
                        {
                            table.Cell().Padding(6).Text(label);
                            table.Cell().Padding(6).AlignRight().Text(value);
                        }

                        Row("Hours (JobLabor)", hours.ToString("N2"));
                        Row("Gross pay", $"R {grossPay:N2}");
                        Row("Deductions (simple)", $"R {deductions:N2}");
                        Row("Net pay", $"R {net:N2}");
                        Row("Labor entries", laborEntryCount.ToString());
                    });

                    col.Item().PaddingTop(16)
                        .Text("Contractor payslip from linked JobLabor. Not a SARS IRP5 / full statutory payroll run.")
                        .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text($"{branding.DisplayName} — Generated {generatedAt:yyyy-MM-dd HH:mm} UTC");
            });
        });

        return doc.GeneratePdf();
    }
}
