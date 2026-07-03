using METERP.Domain;

namespace METERP.Application.Services;

public interface IRecurringJobService
{
    Task<IReadOnlyList<RecurringJobSchedule>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);

    Task<Guid> CreateAsync(RecurringJobSchedule schedule, CancellationToken ct = default);

    Task<int> ProcessDueAsync(CancellationToken ct = default);
}