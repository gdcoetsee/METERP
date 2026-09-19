namespace METERP.Application.Services;

public interface IOperationalReportService
{
    IReadOnlyList<ReportDefinition> GetCatalog();

    Task<ReportTable> RunAsync(string key, Guid? divisionId = null, CancellationToken ct = default);

    Task<JobPerformanceDetail?> GetJobPerformanceAsync(Guid jobId, CancellationToken ct = default);
}

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
