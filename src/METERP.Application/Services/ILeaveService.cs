using METERP.Domain;

namespace METERP.Application.Services;

public interface ILeaveService
{
    Task<decimal> GetAccruedLeaveDaysAsync(Guid employeeId, CancellationToken ct = default);

    Task<decimal> GetAvailableLeaveDaysAsync(Guid employeeId, CancellationToken ct = default);

    Task<Employee?> GetEmployeeForUserAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequest>> GetRequestsForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    Task<Guid> SubmitRequestAsync(LeaveRequest request, CancellationToken ct = default);

    Task<bool> ApproveManagerAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> ApproveExecutiveAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> ApproveHrAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default);

    Task<bool> RejectAsync(Guid requestId, Guid approverUserId, string reason, CancellationToken ct = default);

    Task<IReadOnlyList<LeaveRequest>> GetPendingApprovalsAsync(CancellationToken ct = default);

    /// <summary>Office leave admin: recent requests across employees.</summary>
    Task<IReadOnlyList<LeaveRequest>> GetRecentRequestsAsync(int take = 100, CancellationToken ct = default);

    Task AdjustLeaveBalanceAsync(Guid employeeId, decimal newBalanceDays, string reason, CancellationToken ct = default);
}