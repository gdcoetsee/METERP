using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service for Job / work order management and actual cost tracking.
/// </summary>
public interface IJobService
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
    Task SetCrewAssignmentsAsync(Guid jobId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid jobId, JobStatus newStatus, CancellationToken ct = default);

    // Actual costs (variance tracking)
    Task<Guid> AddCostAsync(JobCost cost, CancellationToken ct = default);
    Task DeleteCostAsync(Guid costId, CancellationToken ct = default);

    // Labor / Timesheets
    Task<Guid> AddLaborAsync(JobLabor labor, CancellationToken ct = default);
    Task DeleteLaborAsync(Guid laborId, CancellationToken ct = default);
}
