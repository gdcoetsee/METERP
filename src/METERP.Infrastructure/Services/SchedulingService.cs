using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

public class SchedulingService : ISchedulingService
{
    private readonly IJobService _jobService;
    private readonly IAssetService _assetService;
    private readonly IEmployeeService _employeeService;

    public SchedulingService(
        IJobService jobService,
        IAssetService assetService,
        IEmployeeService employeeService)
    {
        _jobService = jobService;
        _assetService = assetService;
        _employeeService = employeeService;
    }

    public async Task<SchedulingBoard> GetBoardAsync(int jobPage = 1, int jobPageSize = 50, CancellationToken ct = default)
    {
        var jobs = await _jobService.GetAllAsync(null, jobPage, jobPageSize, ct);
        var assets = await _assetService.GetAllAsync(null, 1, 1000, ct);
        var employees = await _employeeService.GetAllAsync(null, 1, 1000, ct);

        return new SchedulingBoard(jobs, assets, employees);
    }

    public async Task AssignJobResourcesAsync(
        Guid jobId,
        Guid? assetId,
        Guid? leadEmployeeId,
        IReadOnlyList<Guid>? additionalCrewEmployeeIds = null,
        CancellationToken ct = default)
    {
        var job = await _jobService.GetByIdAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} was not found.");

        job.AssetId = assetId;

        var employees = await _employeeService.GetAllAsync(null, 1, 1000, ct);
        var validEmployeeIds = employees.Select(e => e.Id).ToHashSet();

        if (leadEmployeeId.HasValue && leadEmployeeId.Value != Guid.Empty && validEmployeeIds.Contains(leadEmployeeId.Value))
            job.AssignedEmployeeId = leadEmployeeId.Value;
        else
            job.AssignedEmployeeId = null;

        var crewIds = new HashSet<Guid>();
        if (job.AssignedEmployeeId.HasValue)
            crewIds.Add(job.AssignedEmployeeId.Value);

        if (additionalCrewEmployeeIds != null)
        {
            foreach (var id in additionalCrewEmployeeIds.Where(id => id != Guid.Empty && validEmployeeIds.Contains(id)))
                crewIds.Add(id);
        }

        await _jobService.UpdateAsync(job, ct);
        await _jobService.SetCrewAssignmentsAsync(jobId, crewIds.ToList(), ct);
    }
}