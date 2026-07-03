using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class JobService : IJobService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;
    private readonly ITenantCacheService? _cache;
    private readonly IDocumentSequenceService? _documentSequence;

    public JobService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        ITenantCacheService? cache = null,
        IDocumentSequenceService? documentSequence = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _cache = cache;
        _documentSequence = documentSequence;
    }

    public async Task<JobCommandCenterSummary?> GetCommandCenterSummaryAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await GetByIdAsync(jobId, ct);
        if (job == null) return null;

        var costs = job.ActualCosts.Where(c => !c.IsDeleted).ToList();
        var laborCost = job.Labors.Where(l => !l.IsDeleted).Sum(l => l.TotalCost);

        var requisitions = await _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.PurchaseOrder)
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync(ct);

        var poIds = requisitions
            .Where(r => r.PurchaseOrderId.HasValue)
            .Select(r => r.PurchaseOrderId!.Value)
            .Distinct()
            .ToList();

        var grvs = poIds.Count == 0
            ? new List<GoodsReceiptVoucher>()
            : await _dbContext.Set<GoodsReceiptVoucher>()
                .AsNoTracking()
                .Where(g => poIds.Contains(g.PurchaseOrderId))
                .ToListAsync(ct);

        var grvByPo = grvs.GroupBy(g => g.PurchaseOrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ReceivedAt).First().GrvNumber);

        return new JobCommandCenterSummary
        {
            JobId = jobId,
            MaterialCost = costs.Where(c => c.CostType.Equals("Material", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount),
            TravelCost = costs.Where(c => c.CostType.Equals("Travel", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount),
            OtherCost = costs.Where(c => !c.CostType.Equals("Material", StringComparison.OrdinalIgnoreCase)
                && !c.CostType.Equals("Travel", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount),
            LaborCost = laborCost,
            MarginPercent = job.GetMarginPercent(),
            IsReadyToInvoice = job.IsReadyToInvoice(),
            ProgressPercent = job.GetProgressPercent(),
            Requisitions = requisitions.Select(r => new JobRequisitionSummary
            {
                RequisitionNumber = r.RequisitionNumber,
                Status = r.Status,
                PurchaseOrderNumber = r.PurchaseOrder?.PoNumber,
                GrvNumber = r.PurchaseOrderId.HasValue && grvByPo.TryGetValue(r.PurchaseOrderId.Value, out var grv)
                    ? grv
                    : null,
                LineCount = r.Lines.Count(l => !l.IsDeleted)
            }).ToList()
        };
    }

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors)
                .ThenInclude(l => l.Employee)
            .Include(j => j.Customer)
            .Include(j => j.Asset)
            .Include(j => j.AssignedEmployee)
            .Include(j => j.CrewAssignments)
                .ThenInclude(c => c.Employee)
            .Include(j => j.Quote)
                .ThenInclude(q => q != null ? q.Lines : null)
            .Include(j => j.SalesOrder)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }

    public async Task<IReadOnlyList<Job>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                "jobs",
                $"p{page}:s{pageSize}",
                () => LoadJobsAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadJobsAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Job>> LoadJobsAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Job>()
            .AsNoTracking()
            .Include(j => j.Customer)
            .Include(j => j.Asset)
            .Include(j => j.AssignedEmployee)
            .Include(j => j.CrewAssignments)
                .ThenInclude(c => c.Employee)
            .Include(j => j.Quote)
            .Include(j => j.ActualCosts)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(j =>
                j.JobNumber.ToLower().Contains(term) ||
                j.Title.ToLower().Contains(term) ||
                (j.Notes != null && j.Notes.ToLower().Contains(term)) ||
                (j.Customer != null && j.Customer.Name.ToLower().Contains(term)) ||
                (j.Quote != null && j.Quote.QuoteNumber.ToLower().Contains(term)));
        }

        var results = await query
            .OrderByDescending(j => j.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        ListCacheGraphHelper.PrepareJobsForCache(results);
        return results;
    }

    public async Task<Guid> CreateAsync(Job job, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? job.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Job, ct);

        if (string.IsNullOrWhiteSpace(job.JobNumber))
        {
            job.JobNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Job", "J", ct)
                : $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }

        _dbContext.Set<Job>().Add(job);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementJobCountAsync(job.TenantId, ct);
        await InvalidateListCachesAsync(ct);

        return job.Id;
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        _dbContext.Set<Job>().Update(job);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task SetCrewAssignmentsAsync(Guid jobId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} was not found.");

        var distinctIds = employeeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var existing = await _dbContext.Set<JobCrewAssignment>()
            .IgnoreQueryFilters()
            .Where(a => a.JobId == jobId && a.TenantId == job.TenantId)
            .ToListAsync(ct);

        foreach (var row in existing)
            row.IsDeleted = !distinctIds.Contains(row.EmployeeId);

        foreach (var employeeId in distinctIds)
        {
            var row = existing.FirstOrDefault(a => a.EmployeeId == employeeId);
            if (row != null)
            {
                row.IsDeleted = false;
                continue;
            }

            _dbContext.Set<JobCrewAssignment>().Add(new JobCrewAssignment
            {
                JobId = jobId,
                EmployeeId = employeeId,
                TenantId = job.TenantId
            });
        }

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job == null) return;

        foreach (var cost in job.ActualCosts)
        {
            cost.IsDeleted = true;
        }
        job.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid jobId, JobStatus newStatus, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return;

        job.Status = newStatus;

        if (newStatus == JobStatus.Completed || newStatus == JobStatus.Invoiced)
        {
            job.CompletedDate = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task<bool> SignOffAsync(Guid jobId, Guid userId, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null)
            return false;

        job.SignOffStatus = JobSignOffStatus.SignedOff;
        job.SignedOffAt = DateTime.UtcNow;
        job.SignedOffByUserId = userId;

        if (job.Status is JobStatus.Scheduled or JobStatus.InProgress)
            job.Status = JobStatus.Completed;

        job.CompletedDate ??= DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
        return true;
    }

    public async Task<Guid> AddCostAsync(JobCost cost, CancellationToken ct = default)
    {
        _dbContext.Set<JobCost>().Add(cost);
        await _dbContext.SaveChangesAsync(ct);

        // Recalculate job actuals
        var job = await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstOrDefaultAsync(j => j.Id == cost.JobId, ct);

        if (job != null)
        {
            job.ActualCost = job.ActualCosts
                .Where(c => !c.IsDeleted)
                .Sum(c => c.Amount);
            await _dbContext.SaveChangesAsync(ct);
        }

        await InvalidateListCachesAsync(ct);
        return cost.Id;
    }

    public async Task DeleteCostAsync(Guid costId, CancellationToken ct = default)
    {
        var cost = await _dbContext.Set<JobCost>().FirstOrDefaultAsync(c => c.Id == costId, ct);
        if (cost == null) return;

        var jobId = cost.JobId;
        cost.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var job = await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job != null)
        {
            job.ActualCost = job.ActualCosts
                .Where(c => !c.IsDeleted)
                .Sum(c => c.Amount);
            await _dbContext.SaveChangesAsync(ct);
        }

        await InvalidateListCachesAsync(ct);
    }

    public async Task<Guid> AddLaborAsync(JobLabor labor, CancellationToken ct = default)
    {
        await ApplyEmployeeDefaultsAsync(labor, ct);

        _dbContext.Set<JobLabor>().Add(labor);
        await _dbContext.SaveChangesAsync(ct);

        // Optionally update a total labor cost on Job if we add the field later
        var job = await _dbContext.Set<Job>()
            .Include(j => j.Labors)
            .FirstOrDefaultAsync(j => j.Id == labor.JobId, ct);

        if (job != null)
        {
            // For now, we can expose total labor via the collection in UI
            await _dbContext.SaveChangesAsync(ct);
        }

        await InvalidateListCachesAsync(ct);
        return labor.Id;
    }

    public async Task DeleteLaborAsync(Guid laborId, CancellationToken ct = default)
    {
        var labor = await _dbContext.Set<JobLabor>().FirstOrDefaultAsync(l => l.Id == laborId, ct);
        if (labor == null) return;

        var jobId = labor.JobId;
        labor.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var job = await _dbContext.Set<Job>()
            .Include(j => j.Labors)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job != null)
        {
            await _dbContext.SaveChangesAsync(ct);
        }

        await InvalidateListCachesAsync(ct);
    }

    private async Task InvalidateListCachesAsync(CancellationToken ct)
    {
        if (_cache != null)
            await _cache.InvalidateCategoryAsync("jobs", ct);
    }

    private async Task ApplyEmployeeDefaultsAsync(JobLabor labor, CancellationToken ct)
    {
        if (!labor.EmployeeId.HasValue || labor.EmployeeId.Value == Guid.Empty)
            return;

        var employee = await _dbContext.Set<Employee>()
            .FirstOrDefaultAsync(e => e.Id == labor.EmployeeId.Value, ct);

        if (employee == null)
            return;

        if (string.IsNullOrWhiteSpace(labor.Technician))
            labor.Technician = $"{employee.FirstName} {employee.LastName}".Trim();

        if (labor.HourlyRate <= 0)
            labor.HourlyRate = employee.DefaultHourlyRate;
    }

    private async Task TryIncrementJobCountAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementJobCountAsync(tenantId, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break business operations.
        }
    }

    public async Task<IReadOnlyList<JobMilestone>> GetMilestonesAsync(Guid jobId, CancellationToken ct = default) =>
        await _dbContext.Set<JobMilestone>()
            .AsNoTracking()
            .Where(m => m.JobId == jobId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.DueDate)
            .ToListAsync(ct);

    public async Task<Guid> AddMilestoneAsync(JobMilestone milestone, CancellationToken ct = default)
    {
        _dbContext.Set<JobMilestone>().Add(milestone);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
        return milestone.Id;
    }

    public async Task UpdateMilestoneAsync(JobMilestone milestone, CancellationToken ct = default)
    {
        _dbContext.Set<JobMilestone>().Update(milestone);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task DeleteMilestoneAsync(Guid milestoneId, CancellationToken ct = default)
    {
        var milestone = await _dbContext.Set<JobMilestone>().FirstOrDefaultAsync(m => m.Id == milestoneId, ct);
        if (milestone == null) return;
        milestone.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task<IReadOnlyList<JobSnagItem>> GetSnagsAsync(Guid jobId, CancellationToken ct = default) =>
        await _dbContext.Set<JobSnagItem>()
            .AsNoTracking()
            .Where(s => s.JobId == jobId)
            .OrderByDescending(s => s.ReportedAt)
            .ToListAsync(ct);

    public async Task<Guid> AddSnagAsync(JobSnagItem snag, CancellationToken ct = default)
    {
        _dbContext.Set<JobSnagItem>().Add(snag);
        await _dbContext.SaveChangesAsync(ct);
        return snag.Id;
    }

    public async Task ResolveSnagAsync(Guid snagId, Guid userId, CancellationToken ct = default)
    {
        var snag = await _dbContext.Set<JobSnagItem>().FirstOrDefaultAsync(s => s.Id == snagId, ct);
        if (snag == null || snag.IsResolved) return;
        snag.IsResolved = true;
        snag.ResolvedAt = DateTime.UtcNow;
        snag.ResolvedByUserId = userId;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JobSafetyIncident>> GetSafetyIncidentsAsync(Guid jobId, CancellationToken ct = default) =>
        await _dbContext.Set<JobSafetyIncident>()
            .AsNoTracking()
            .Where(i => i.JobId == jobId)
            .OrderByDescending(i => i.ReportedAt)
            .ToListAsync(ct);

    public async Task<Guid> AddSafetyIncidentAsync(JobSafetyIncident incident, CancellationToken ct = default)
    {
        _dbContext.Set<JobSafetyIncident>().Add(incident);
        await _dbContext.SaveChangesAsync(ct);
        return incident.Id;
    }

    public async Task CloseSafetyIncidentAsync(Guid incidentId, Guid userId, string? correctiveAction, CancellationToken ct = default)
    {
        var incident = await _dbContext.Set<JobSafetyIncident>().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident == null || incident.IsClosed) return;
        incident.IsClosed = true;
        incident.ClosedAt = DateTime.UtcNow;
        incident.ClosedByUserId = userId;
        if (!string.IsNullOrWhiteSpace(correctiveAction))
            incident.CorrectiveAction = correctiveAction.Trim();
        await _dbContext.SaveChangesAsync(ct);
    }
}
