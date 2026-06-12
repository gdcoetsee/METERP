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
}

public sealed record SchedulingBoard(
    IReadOnlyList<Job> Jobs,
    IReadOnlyList<Asset> Assets,
    IReadOnlyList<Employee> Employees);