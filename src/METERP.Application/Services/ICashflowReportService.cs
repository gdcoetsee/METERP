namespace METERP.Application.Services;

/// <summary>
/// Cashflow forecast from outstanding receivables, accepted quote pipeline, and open PO commitments.
/// </summary>
public interface ICashflowReportService
{
    Task<CashflowForecastSummary> GetCashflowForecastAsync(CancellationToken ct = default);
}

public sealed record CashflowForecastSummary(
    decimal ReceivableInflow,
    int ReceivableInvoiceCount,
    decimal PipelineInflow,
    int PipelineQuoteCount,
    decimal CommittedOutflow,
    int OpenPurchaseOrderCount,
    decimal NetForecastInflow,
    decimal InflowSharePercent);