using METERP.Domain;

namespace METERP.Application.Services;

public interface IRecurringJobService
{
    Task<IReadOnlyList<RecurringJobSchedule>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);

    Task<RecurringJobSchedule?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Guid> CreateAsync(RecurringJobSchedule schedule, CancellationToken ct = default);

    Task UpdateAsync(RecurringJobSchedule schedule, CancellationToken ct = default);

    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Spawns jobs for due schedules. Continues on per-schedule failures (e.g. quota)
    /// so one bad schedule does not block the rest of the batch.
    /// </summary>
    Task<int> ProcessDueAsync(CancellationToken ct = default);
}
