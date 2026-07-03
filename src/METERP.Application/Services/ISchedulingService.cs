using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Job scheduling board: list jobs and assign assets/employees (contractor operations).
/// </summary>
public interface ISchedulingService
{
    Task<SchedulingBoard> GetBoardAsync(int jobPage = 1, int jobPageSize = 50, CancellationToken ct = default);

    Task AssignJobResourcesAsync(
        Guid jobId,
        Guid? assetId,
        Guid? leadEmployeeId,
        IReadOnlyList<Guid>? additionalCrewEmployeeIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Logs JobLabor for assigned crew (lead + crew assignments) in one action.
    /// </summary>
    Task<CrewLaborAddResult> AddCrewLaborAsync(
        Guid jobId,
        decimal hours,
        DateTime? workDate = null,
        IReadOnlyList<Guid>? crewEmployeeIds = null,
        string? description = null,
        CancellationToken ct = default);

    Task UpdateScheduledStartAsync(Guid jobId, DateTime? scheduledStart, CancellationToken ct = default);

    Task<IReadOnlyList<Job>> GetCalendarJobsAsync(DateTime weekStart, int dayCount = 7, CancellationToken ct = default);
}

public sealed record CrewLaborAddResult(int EntriesAdded, IReadOnlyList<Guid> LaborIds);

public sealed record SchedulingBoard(
    IReadOnlyList<Job> Jobs,
    IReadOnlyList<Asset> Assets,
    IReadOnlyList<Employee> Employees);