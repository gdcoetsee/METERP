using METERP.Domain;

namespace METERP.Application.Services;

public interface IPpeIssueService
{
    Task<IReadOnlyList<EmployeePpeIssue>> GetHistoryAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);

    Task RecordFromRequisitionIssueAsync(StockRequisition requisition, CancellationToken ct = default);
}