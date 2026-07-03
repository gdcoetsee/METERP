using METERP.Application.Models;

namespace METERP.Application.Services;

public interface IAccountabilityReportService
{
    Task<IReadOnlyList<DivisionScorecardRow>> GetDivisionScorecardsAsync(CancellationToken ct = default);

    Task<string> ExportDivisionScorecardsCsvAsync(CancellationToken ct = default);

    Task<IReadOnlyList<UserActivityRow>> GetUserActivityAsync(int days = 30, CancellationToken ct = default);

    Task<string> ExportUserActivityCsvAsync(int days = 30, CancellationToken ct = default);

    Task<IReadOnlyList<OverdueApprovalRow>> GetOverdueApprovalsAsync(CancellationToken ct = default);

    Task<string> ExportOverdueApprovalsCsvAsync(CancellationToken ct = default);
}