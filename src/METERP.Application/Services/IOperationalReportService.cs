namespace METERP.Application.Services;

public interface IOperationalReportService
{
    IReadOnlyList<ReportDefinition> GetCatalog();

    Task<ReportTable> RunAsync(string key, Guid? divisionId = null, CancellationToken ct = default);

    /// <summary>
    /// Tenant-wide counts for the reports snapshot. Does not load a page of documents.
    /// </summary>
    Task<OperationalSnapshot> GetLiveSnapshotAsync(CancellationToken ct = default);

    Task<JobPerformanceDetail?> GetJobPerformanceAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>Live totals for the reports page cards. Credit notes reduce outstanding value.</summary>
public sealed record OperationalSnapshot(
    int TotalQuotes,
    int AcceptedQuotes,
    int ActiveJobs,
    decimal ActiveJobsQuoted,
    int OutstandingInvoices,
    decimal OutstandingInvoiceValue,
    int TotalItems,
    int LowStockItems,
    int TotalAssets,
    int OperationalAssets,
    int TotalSuppliers,
    int OpenPurchaseOrders,
    decimal OpenPurchaseOrderValue,
    int TotalSalesOrders,
    int ConfirmedSalesOrders,
    int ActiveEmployees);

public sealed record ReportDefinition(
    string Key,
    string Category,
    string Title,
    string Description,
    bool SupportsDivision,
    string TestId);

public sealed record ReportTable(
    string Title,
    string[] Headers,
    IReadOnlyList<string[]> Rows,
    string? Summary = null,
    IReadOnlyList<Guid>? RowIds = null);

public sealed record JobPerformanceCostLine(string Type, string Description, decimal Amount, DateTime Date);

public sealed record JobPerformanceLaborLine(string Technician, decimal Hours, decimal Rate, decimal Total, DateTime Date);

public sealed record JobPerformanceDetail(
    Guid JobId,
    string JobNumber,
    string Title,
    string Customer,
    string Division,
    string Status,
    string SignOff,
    decimal Quoted,
    decimal Labor,
    decimal Travel,
    decimal Materials,
    decimal Other,
    decimal Actual,
    decimal Variance,
    decimal MarginPercent,
    DateTime? ScheduledStart,
    DateTime? CompletedDate,
    DateTime? ClosedAt,
    string Assigned,
    IReadOnlyList<JobPerformanceCostLine> Costs,
    IReadOnlyList<JobPerformanceLaborLine> LaborLines);
