using Microsoft.EntityFrameworkCore;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Same-calendar-day exclusive booking for assets and crew on open jobs.
/// Jobs without a scheduled start do not conflict.
/// </summary>
internal static class SchedulingConflictGuard
{
    public static async Task EnsureAssetAvailableAsync(
        AppDbContext db,
        Guid jobId,
        Guid? assetId,
        DateTime? scheduledStart,
        CancellationToken ct)
    {
        if (assetId is not { } aid || aid == Guid.Empty || scheduledStart is null)
            return;

        var date = scheduledStart.Value.Date;
        var candidates = await db.Set<Job>()
            .AsNoTracking()
            .Where(j =>
                j.Id != jobId
                && j.AssetId == aid
                && j.ScheduledStart.HasValue
                && j.Status != JobStatus.Closed
                && j.Status != JobStatus.Cancelled)
            .Select(j => new { j.JobNumber, j.ScheduledStart })
            .ToListAsync(ct);

        var conflict = candidates.FirstOrDefault(j => j.ScheduledStart!.Value.Date == date);
        if (conflict == null)
            return;

        var assetName = await db.Set<Asset>().AsNoTracking()
            .Where(a => a.Id == aid)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(ct);

        var label = string.IsNullOrWhiteSpace(assetName) ? "Asset" : $"Asset '{assetName}'";
        throw new InvalidOperationException(
            $"{label} is already scheduled on job {conflict.JobNumber} on {date:yyyy-MM-dd}.");
    }

    public static async Task EnsureEmployeesAvailableAsync(
        AppDbContext db,
        Guid jobId,
        IEnumerable<Guid> employeeIds,
        DateTime? scheduledStart,
        CancellationToken ct)
    {
        var ids = employeeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0 || scheduledStart is null)
            return;

        var date = scheduledStart.Value.Date;
        var otherJobs = await db.Set<Job>()
            .AsNoTracking()
            .Where(j =>
                j.Id != jobId
                && j.ScheduledStart.HasValue
                && j.Status != JobStatus.Closed
                && j.Status != JobStatus.Cancelled)
            .Select(j => new { j.Id, j.JobNumber, j.AssignedEmployeeId, j.ScheduledStart })
            .ToListAsync(ct);

        var sameDay = otherJobs.Where(j => j.ScheduledStart!.Value.Date == date).ToList();
        if (sameDay.Count == 0)
            return;

        var sameDayIds = sameDay.Select(j => j.Id).ToList();
        var crewHits = await db.Set<JobCrewAssignment>()
            .AsNoTracking()
            .Where(a => sameDayIds.Contains(a.JobId) && ids.Contains(a.EmployeeId))
            .Select(a => new { a.EmployeeId, a.JobId })
            .ToListAsync(ct);

        Guid? conflictingEmployeeId = null;
        string? conflictingJobNumber = null;

        foreach (var job in sameDay)
        {
            if (job.AssignedEmployeeId is { } lead && ids.Contains(lead))
            {
                conflictingEmployeeId = lead;
                conflictingJobNumber = job.JobNumber;
                break;
            }
        }

        if (conflictingEmployeeId == null && crewHits.Count > 0)
        {
            var hit = crewHits[0];
            conflictingEmployeeId = hit.EmployeeId;
            conflictingJobNumber = sameDay.First(j => j.Id == hit.JobId).JobNumber;
        }

        if (conflictingEmployeeId == null || conflictingJobNumber == null)
            return;

        var emp = await db.Set<Employee>().AsNoTracking()
            .Where(e => e.Id == conflictingEmployeeId.Value)
            .Select(e => new { e.FirstName, e.LastName })
            .FirstOrDefaultAsync(ct);

        var empLabel = emp == null
            ? "Employee"
            : $"Employee '{emp.FirstName} {emp.LastName}'".Trim();
        throw new InvalidOperationException(
            $"{empLabel} is already scheduled on job {conflictingJobNumber} on {date:yyyy-MM-dd}.");
    }
}
