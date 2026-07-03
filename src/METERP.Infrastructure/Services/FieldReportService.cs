using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class FieldReportService : IFieldReportService
{
    private readonly AppDbContext _dbContext;
    private readonly IJobService _jobService;
    private readonly IAuditService? _audit;
    private readonly ITenantNotificationService? _notifications;

    public FieldReportService(
        AppDbContext dbContext,
        IJobService jobService,
        IAuditService? audit = null,
        ITenantNotificationService? notifications = null)
    {
        _dbContext = dbContext;
        _jobService = jobService;
        _audit = audit;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<FieldReport>> GetPendingAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<FieldReport>()
            .AsNoTracking()
            .Include(r => r.Job).ThenInclude(j => j!.Customer)
            .Where(r => r.Status == FieldReportStatus.PendingApproval)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FieldReport>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _dbContext.Set<FieldReport>()
            .AsNoTracking()
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<Guid> SubmitAsync(FieldReport report, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == report.JobId, ct);
        if (job == null)
            throw new InvalidOperationException("Job not found.");

        if (report.HoursWorked < 0 || report.TravelCost < 0)
            throw new InvalidOperationException("Hours and travel cost cannot be negative.");

        report.Status = FieldReportStatus.PendingApproval;
        report.SubmittedAt = DateTime.UtcNow;

        _dbContext.Set<FieldReport>().Add(report);
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("SUBMIT", "FieldReport", job.JobNumber,
                $"{report.HoursWorked:N1}h travel R{report.TravelCost:N2}", ct);

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                Title = "Field report submitted",
                Message = $"{job.JobNumber}: {report.HoursWorked:N1}h, travel R{report.TravelCost:N2} — awaiting approval.",
                Category = "field",
                TargetRoles = "Admin,Executive,Division Manager",
                RelatedEntityId = report.Id,
                RelatedEntityType = "FieldReport"
            }, ct);
        }

        return report.Id;
    }

    public async Task<bool> ApproveAsync(Guid reportId, Guid approverUserId, CancellationToken ct = default)
    {
        var report = await _dbContext.Set<FieldReport>()
            .Include(r => r.Job).ThenInclude(j => j!.AssignedEmployee)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct);

        if (report == null || report.Status != FieldReportStatus.PendingApproval)
            return false;

        var hourlyRate = report.Job?.AssignedEmployee?.DefaultHourlyRate ?? 195m;

        if (report.HoursWorked > 0)
        {
            await _jobService.AddLaborAsync(new JobLabor
            {
                JobId = report.JobId,
                WorkDate = report.WorkDate,
                Hours = report.HoursWorked,
                HourlyRate = hourlyRate,
                EmployeeId = report.Job?.AssignedEmployeeId,
                Technician = report.Job?.AssignedEmployee != null
                    ? $"{report.Job.AssignedEmployee.FirstName} {report.Job.AssignedEmployee.LastName}".Trim()
                    : "Field technician",
                Description = $"Field report {report.SubmittedAt:yyyy-MM-dd}"
            }, ct);
        }

        if (report.TravelCost > 0)
        {
            await _jobService.AddCostAsync(new JobCost
            {
                JobId = report.JobId,
                Amount = report.TravelCost,
                CostType = "Travel",
                Description = $"Field travel — {report.SubmittedAt:yyyy-MM-dd}",
                CostDate = report.WorkDate
            }, ct);
        }

        report.Status = FieldReportStatus.Approved;
        report.ApprovedByUserId = approverUserId;
        report.ApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("APPROVE", "FieldReport", report.Job?.JobNumber ?? report.Id.ToString(),
                $"Posted {report.HoursWorked:N1}h + travel R{report.TravelCost:N2}", ct);

        return true;
    }

    public async Task<bool> RejectAsync(Guid reportId, Guid approverUserId, string reason, CancellationToken ct = default)
    {
        var report = await _dbContext.Set<FieldReport>().FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report == null || report.Status != FieldReportStatus.PendingApproval)
            return false;

        report.Status = FieldReportStatus.Rejected;
        report.ApprovedByUserId = approverUserId;
        report.ApprovedAt = DateTime.UtcNow;
        report.RejectionReason = reason;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("REJECT", "FieldReport", report.Id.ToString(), reason, ct);

        return true;
    }
}