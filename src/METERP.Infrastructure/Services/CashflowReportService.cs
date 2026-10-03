using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class CashflowReportService : ICashflowReportService
{
    private readonly AppDbContext _dbContext;

    public CashflowReportService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<CashflowForecastSummary> GetCashflowForecastAsync(CancellationToken ct = default)
    {
        var receivableStatuses = new[]
        {
            InvoiceStatus.Sent,
            InvoiceStatus.PartiallyPaid,
            InvoiceStatus.Overdue
        };

        var receivableInvoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => receivableStatuses.Contains(i.Status))
            .ToListAsync(ct);

        var pipelineQuotes = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Where(q => q.Status == QuoteStatus.Accepted)
            .ToListAsync(ct);

        var openPurchaseOrders = await _dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Where(p => p.Status != PurchaseOrderStatus.Received && p.Status != PurchaseOrderStatus.Cancelled)
            .ToListAsync(ct);

        // Credit notes are stored positive and reduce receivable inflow. Sales invoices stay on Total.
        var receivableInflow = receivableInvoices.Sum(i =>
            i.DocumentType == InvoiceDocumentType.CreditNote ? -Math.Abs(i.Total) : i.Total);
        var receivableCount = receivableInvoices.Count(i => i.DocumentType != InvoiceDocumentType.CreditNote);
        var pipelineInflow = pipelineQuotes.Sum(q => q.Total);
        var committedOutflow = openPurchaseOrders.Sum(p => p.Total);
        var grossInflow = receivableInflow + pipelineInflow;
        var netForecast = grossInflow - committedOutflow;

        var inflowShare = grossInflow + committedOutflow > 0
            ? Math.Round(grossInflow / (grossInflow + committedOutflow) * 100m, 1)
            : 0m;

        return new CashflowForecastSummary(
            receivableInflow,
            receivableCount,
            pipelineInflow,
            pipelineQuotes.Count,
            committedOutflow,
            openPurchaseOrders.Count,
            netForecast,
            inflowShare);
    }
}