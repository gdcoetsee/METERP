using METERP.Domain;

namespace METERP.Application.Services;

public interface IPpeIssueService
{
    Task<IReadOnlyList<EmployeePpeIssue>> GetHistoryAsync(
        Guid? employeeId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Issues PPE stock to an employee register entry and decrements inventory.
    /// JobId is optional site context only.
    /// </summary>
    Task<Guid> IssueToEmployeeAsync(
        Guid employeeId,
        Guid inventoryItemId,
        decimal quantity,
        Guid issuedByUserId,
        Guid? jobId = null,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>Records PPE register rows when a PPE-flagged job requisition is issued (optional job context).</summary>
    Task RecordFromRequisitionIssueAsync(StockRequisition requisition, CancellationToken ct = default);
}
