using METERP.Application.Models;
using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service for Job / work order management and actual cost tracking.
/// </summary>
public interface IJobService
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<JobCommandCenterSummary?> GetCommandCenterSummaryAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task SetCrewAssignmentsAsync(Guid jobId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid jobId, JobStatus newStatus, CancellationToken ct = default);

    /// <summary>
    /// Advances dual work sign-off one step:
    /// None → PendingManager → PendingExecutive → SignedOff.
    /// </summary>
    Task<bool> AdvanceWorkSignOffAsync(Guid jobId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Completes full work sign-off in one step (demo/E2E/spine). Prefer <see cref="AdvanceWorkSignOffAsync"/> in UI.
    /// </summary>
    Task<bool> SignOffAsync(Guid jobId, Guid userId, CancellationToken ct = default);

    Task<bool> CloseAsync(Guid jobId, Guid executiveUserId, string? notes, CancellationToken ct = default);

    Task<bool> ReopenAsync(Guid jobId, Guid executiveUserId, string reason, CancellationToken ct = default);

    /// <summary>Voids an open job (not closed). Blocks further ops; reason required.</summary>
    Task<bool> CancelAsync(Guid jobId, Guid userId, string reason, CancellationToken ct = default);

    /// <summary>Recomputes <see cref="Job.ActualCost"/> from active <see cref="JobCost"/> rows (labor is tracked separately).</summary>
    Task RecalculateActualCostAsync(Guid jobId, CancellationToken ct = default);

    // Actual costs (variance tracking)
    Task<Guid> AddCostAsync(JobCost cost, CancellationToken ct = default);
    Task DeleteCostAsync(Guid costId, CancellationToken ct = default);

    // Labor / Timesheets
    Task<Guid> AddLaborAsync(JobLabor labor, CancellationToken ct = default);
    Task DeleteLaborAsync(Guid laborId, CancellationToken ct = default);

    // Milestones & snag list
    Task<IReadOnlyList<JobMilestone>> GetMilestonesAsync(Guid jobId, CancellationToken ct = default);
    Task<Guid> AddMilestoneAsync(JobMilestone milestone, CancellationToken ct = default);
    Task UpdateMilestoneAsync(JobMilestone milestone, CancellationToken ct = default);
    Task DeleteMilestoneAsync(Guid milestoneId, CancellationToken ct = default);

    Task<IReadOnlyList<JobSnagItem>> GetSnagsAsync(Guid jobId, CancellationToken ct = default);
    Task<Guid> AddSnagAsync(JobSnagItem snag, CancellationToken ct = default);
    Task ResolveSnagAsync(Guid snagId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<JobSafetyIncident>> GetSafetyIncidentsAsync(Guid jobId, CancellationToken ct = default);
    Task<Guid> AddSafetyIncidentAsync(JobSafetyIncident incident, CancellationToken ct = default);
    Task CloseSafetyIncidentAsync(Guid incidentId, Guid userId, string? correctiveAction, CancellationToken ct = default);
}
