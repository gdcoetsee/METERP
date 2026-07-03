using METERP.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using METERP.Domain;

namespace METERP.Web.Reports;

/// <summary>
/// Generates professional, tenant-branded PDF reports for the AI Copilot.
/// Emphasizes SA realities (Rands, 15% VAT, explicit travel costs).
/// Uses QuestPDF community license (free for this use).
/// </summary>
public static class AiReportGenerator
{
    private const string FooterTagline =
        "Currency: South African Rand (R)  •  VAT: 15%  •  Travel costs tracked explicitly on every Job via JobCost (type 'Travel')";

    static AiReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] GenerateCopilotSessionReport(
        string title,
        List<(string Question, string Response)> history,
        string? tenantName = null,
        string? extraFooterNote = null,
        TenantBranding? branding = null)
    {
        branding ??= TenantBranding.Default;
        var displayName = tenantName ?? branding.DisplayName;
        var brandColor = branding.ColorHex;
        var generatedAt = DateTime.UtcNow;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(595.28f, 841.89f);
                page.Margin(35);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{displayName} — AI Copilot Report").SemiBold().FontSize(16).FontColor(brandColor);
                    col.Item().Text(title).FontSize(12).FontColor("#334155");
                    col.Item().Text($"Generated: {generatedAt:yyyy-MM-dd HH:mm} UTC  •  Tenant: {displayName}").FontSize(9).FontColor("#64748b");
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(brandColor);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    if (history == null || history.Count == 0)
                    {
                        col.Item().Text("No AI interactions recorded in this session.").Italic();
                    }
                    else
                    {
                        for (int i = 0; i < history.Count; i++)
                        {
                            var (q, a) = history[i];

                            col.Item().PaddingTop(i == 0 ? 0 : 8).Text($"Q{i + 1}: {q}").Bold().FontSize(10);
                            col.Item().PaddingTop(2).Text(a).FontSize(9.5f).LineHeight(1.35f);
                            col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor("#e2e8f0");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(extraFooterNote))
                    {
                        col.Item().PaddingTop(12).Text(extraFooterNote).FontSize(9).Italic().FontColor("#475569");
                    }
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().Text($"{displayName} • Contractor ERP")
                        .FontSize(8).FontColor("#64748b");
                    f.Item().Text(FooterTagline).FontSize(7).FontColor("#94a3b8");
                    f.Item().PaddingTop(2).Text("This report was produced by the AI-native co-pilot that thinks from your live ERP data (labor rates, travel history, inventory, variance).")
                        .FontSize(7).FontColor("#94a3b8");
                });
            });
        });

        return doc.GeneratePdf();
    }

    public static byte[] GenerateSingleResponseReport(
        string query,
        string response,
        string? tenantName = null,
        TenantBranding? branding = null)
    {
        var hist = new List<(string, string)> { (query ?? "User query", response ?? "") };
        return GenerateCopilotSessionReport("Single AI Response", hist, tenantName, branding: branding);
    }

    public static byte[] GenerateQuotePdf(
        Quote quote,
        string? aiNotes = null,
        string? tenantName = null,
        TenantBranding? branding = null)
    {
        branding ??= TenantBranding.Default;
        var displayName = tenantName ?? branding.DisplayName;
        var brandColor = branding.ColorHex;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(595.28f, 841.89f);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{displayName} — Professional Quote").SemiBold().FontSize(16).FontColor(brandColor);
                    col.Item().Text($"{quote.QuoteNumber}  •  {quote.QuoteDate:yyyy-MM-dd}  •  Valid to {quote.ValidUntil:yyyy-MM-dd}").FontSize(10);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(brandColor);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text("Bill To:").Bold();
                    col.Item().Text(quote.Customer?.Name ?? "Customer");

                    col.Item().PaddingTop(8).Text("Scope / Notes").Bold();
                    col.Item().Text(string.IsNullOrWhiteSpace(quote.Notes) ? "(See AI Copilot for detailed scope & line suggestions)" : quote.Notes);

                    if (!string.IsNullOrWhiteSpace(aiNotes))
                    {
                        col.Item().PaddingTop(6).Text("AI-Enhanced Scope / Recommendations").Bold().FontColor("#334155");
                        col.Item().Text(aiNotes);
                    }

                    col.Item().PaddingTop(10).Text("Totals (incl. 15% SA VAT)").Bold();
                    col.Item().Text($"Subtotal: R {quote.Subtotal:N2}");
                    col.Item().Text($"Tax (15%): R {quote.Tax:N2}");
                    col.Item().Text($"Total: R {quote.Total:N2}").Bold().FontSize(12);

                    col.Item().PaddingTop(8).Text("Line items are best created and priced via the AI Copilot (includes realistic travel, labor, and material rates for SA contracting).").FontSize(9).Italic().FontColor("#64748b");
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().Text($"{displayName} • Contractor ERP").FontSize(8).FontColor("#64748b");
                    f.Item().Text(FooterTagline).FontSize(7).FontColor("#94a3b8");
                });
            });
        });

        return doc.GeneratePdf();
    }

    public static byte[] GenerateJobCloseoutPdf(
        Job job,
        string? aiNotes = null,
        string? tenantName = null,
        TenantBranding? branding = null)
    {
        branding ??= TenantBranding.Default;
        var displayName = tenantName ?? branding.DisplayName;
        var brandColor = branding.ColorHex;

        var laborTotal = (job.Labors?.Where(l => !l.IsDeleted).Sum(l => l.TotalCost) ?? 0m);
        var costs = job.ActualCosts?.Where(c => !c.IsDeleted).ToList() ?? new List<JobCost>();
        var travelTotal = costs.Where(c => c.CostType == "Travel").Sum(c => c.Amount);
        var otherCosts = costs.Where(c => c.CostType != "Travel").Sum(c => c.Amount);
        var actualTotal = job.ActualCost + laborTotal;
        var variance = actualTotal - job.QuotedTotal;

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(595.28f, 841.89f);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{displayName} — Job Closeout Report").SemiBold().FontSize(16).FontColor(brandColor);
                    col.Item().Text($"{job.JobNumber} — {job.Title}  •  Status: {job.Status}").FontSize(10);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(brandColor);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Text("Customer / Asset").Bold();
                    col.Item().Text(job.Customer?.Name ?? "—");
                    if (job.Asset != null) col.Item().Text($"Asset: {job.Asset.Name} ({job.Asset.AssetNumber})");

                    col.Item().PaddingTop(8).Text("Financial Summary (R)").Bold();
                    col.Item().Text($"Quoted Total: R {job.QuotedTotal:N2}");
                    col.Item().Text($"Materials Actual: R {job.ActualCost:N2}");
                    col.Item().Text($"Labor Total: R {laborTotal:N2}");
                    col.Item().Text($"  (incl. explicit Travel: R {travelTotal:N2})");
                    col.Item().Text($"Other Costs: R {otherCosts:N2}");
                    col.Item().Text($"Actual Total: R {actualTotal:N2}").Bold();
                    var varColor = variance > 0 ? "over" : variance < 0 ? "under" : "on";
                    col.Item().Text($"Variance: R {variance:N2} ({varColor} budget)").Bold().FontSize(11);

                    if (!string.IsNullOrWhiteSpace(job.Notes))
                    {
                        col.Item().PaddingTop(8).Text("Job Notes").Bold();
                        col.Item().Text(job.Notes);
                    }

                    if (!string.IsNullOrWhiteSpace(aiNotes))
                    {
                        col.Item().PaddingTop(6).Text("AI Closeout Analysis / Recommendations").Bold().FontColor("#334155");
                        col.Item().Text(aiNotes);
                    }

                    col.Item().PaddingTop(8).Text("Cost breakdown and labor details are recorded in the system. AI analysis factors travel, labor rates, and material consumption for margin recommendations.").FontSize(9).Italic().FontColor("#64748b");
                });

                page.Footer().AlignCenter().Column(f =>
                {
                    f.Item().Text($"{displayName} • Contractor ERP").FontSize(8).FontColor("#64748b");
                    f.Item().Text(FooterTagline).FontSize(7).FontColor("#94a3b8");
                });
            });
        });

        return doc.GeneratePdf();
    }
}