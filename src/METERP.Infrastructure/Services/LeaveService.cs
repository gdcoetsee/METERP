using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class LeaveService : ILeaveService
{
    private readonly AppDbContext _dbContext;

    public LeaveService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<decimal> GetAccruedLeaveDaysAsync(Guid employeeId, CancellationToken ct = default)
    {
        var emp = await _dbContext.Set<Employee>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (emp == null) return 0;

        return LeaveAccrualCalculator.CalculateAccruedDays(
            emp.AnnualLeaveEntitlementDays,
            emp.HireDate,
            DateTime.UtcNow);
    }

    public async Task<decimal> GetAvailableLeaveDaysAsync(Guid employeeId, CancellationToken ct = default)
    {
        var emp = await _dbContext.Set<Employee>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (emp == null) return 0;

        var accrued = await GetAccruedLeaveDaysAsync(employeeId, ct);
        var taken = await _dbContext.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.Status == LeaveRequestStatus.Approved && r.IsPaid)
            .SumAsync(r => r.DaysRequested, ct);

        return accrued - taken + emp.LeaveBalanceDays;
    }

    public async Task<Employee?> GetEmployeeForUserAsync(Guid userId, CancellationToken ct = default)
    {
        return await _dbContext.Set<Employee>()
            .FirstOrDefaultAsync(e => e.LinkedUserId == userId && e.IsActive, ct);
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetRequestsForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        return await _dbContext.Set<LeaveRequest>()
            .AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<Guid> SubmitRequestAsync(LeaveRequest request, CancellationToken ct = default)
    {
        if (request.DaysRequested <= 0)
        {
            request.DaysRequested = LeaveAccrualCalculator.CalculateBusinessDays(
                request.StartDate,
                request.EndDate);
        }

        if (request.IsPaid)
        {
            var available = await GetAvailableLeaveDaysAsync(request.EmployeeId, ct);
            if (request.DaysRequested > available)
                throw new InvalidOperationException(
                    $"Insufficient leave balance. Available: {available:N1} days, requested: {request.DaysRequested:N1}.");
        }

        request.Status = LeaveRequestStatus.PendingManager;

        // Stamp tenant from employee so field-portal circuits never insert Guid.Empty TenantId.
        if (request.TenantId == Guid.Empty)
        {
            var emp = await _dbContext.Set<Employee>().AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
            if (emp != null)
                request.TenantId = emp.TenantId;
        }

        _dbContext.Set<LeaveRequest>().Add(request);
        await _dbContext.SaveChangesAsync(ct);
        return request.Id;
    }

    public async Task<bool> ApproveManagerAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default)
    {
        var request = await _dbContext.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null || request.Status != LeaveRequestStatus.PendingManager)
            return false;

        request.Status = LeaveRequestStatus.PendingExecutive;
        request.ManagerApprovedByUserId = approverUserId;
        request.ManagerApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ApproveExecutiveAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default)
    {
        var request = await _dbContext.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null || request.Status != LeaveRequestStatus.PendingExecutive)
            return false;

        request.Status = LeaveRequestStatus.PendingHr;
        request.ExecutiveApprovedByUserId = approverUserId;
        request.ExecutiveApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ApproveHrAsync(Guid requestId, Guid approverUserId, CancellationToken ct = default)
    {
        var request = await _dbContext.Set<LeaveRequest>()
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null || request.Status != LeaveRequestStatus.PendingHr)
            return false;

        request.Status = LeaveRequestStatus.Approved;
        request.HrApprovedByUserId = approverUserId;
        request.HrApprovedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetPendingApprovalsAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<LeaveRequest>()
            .AsNoTracking()
            .Include(r => r.Employee)
            .Where(r => r.Status == LeaveRequestStatus.PendingManager
                || r.Status == LeaveRequestStatus.PendingExecutive
                || r.Status == LeaveRequestStatus.PendingHr)
            .OrderBy(r => r.StartDate)
            .ToListAsync(ct);
    }

    public async Task<bool> RejectAsync(Guid requestId, Guid approverUserId, string reason, CancellationToken ct = default)
    {
        var request = await _dbContext.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null || request.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected or LeaveRequestStatus.Cancelled)
            return false;

        request.Status = LeaveRequestStatus.Rejected;
        request.RejectionReason = reason;
        request.LastModifiedBy = approverUserId.ToString();
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<LeaveRequest>> GetRecentRequestsAsync(int take = 100, CancellationToken ct = default)
    {
        return await _dbContext.Set<LeaveRequest>()
            .AsNoTracking()
            .Include(r => r.Employee)
            .OrderByDescending(r => r.CreatedDate)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task AdjustLeaveBalanceAsync(
        Guid employeeId,
        decimal newBalanceDays,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required for leave balance adjustment.", nameof(reason));

        var emp = await _dbContext.Set<Employee>().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new InvalidOperationException("Employee not found.");

        var previous = emp.LeaveBalanceDays;
        emp.LeaveBalanceDays = newBalanceDays;
        emp.Notes = string.IsNullOrWhiteSpace(emp.Notes)
            ? $"[Leave adj] {reason.Trim()} (was {previous:N1})"
            : emp.Notes + $"\n[Leave adj {DateTime.UtcNow:yyyy-MM-dd}] {reason.Trim()} (was {previous:N1})";

        await _dbContext.SaveChangesAsync(ct);
    }
}