namespace METERP.Application.Services;

/// <summary>
/// Job-level analytics derived from quoted vs actual costs (variance / margin).
/// </summary>
public interface IJobReportService
{
    Task<JobProfitabilitySummary> GetJobProfitabilitySummaryAsync(CancellationToken ct = default);
}

public sealed record JobProfitabilitySummary(
    decimal AverageMarginPercent,
    int JobsAnalyzed,
    JobProfitabilityRow? TopPerformer);

public sealed record JobProfitabilityRow(
    Guid JobId,
    string JobNumber,
    string Title,
    decimal QuotedTotal,
    decimal ActualTotal,
    decimal Variance,
    decimal MarginPercent);