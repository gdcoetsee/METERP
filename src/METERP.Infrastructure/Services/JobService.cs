using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class JobService : IJobService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;
    private readonly ITenantCacheService? _cache;

    public JobService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _cache = cache;
    }

    public async Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors)
            .Include(j => j.Customer)
            .Include(j => j.Asset)
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

        return await query
            .OrderByDescending(j => j.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Job job, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? job.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Job, ct);

        if (string.IsNullOrWhiteSpace(job.JobNumber))
        {
            job.JobNumber = $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        _dbContext.Set<Job>().Add(job);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementJobCountAsync(job.TenantId, ct);
        InvalidateListCaches();

        return job.Id;
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        _dbContext.Set<Job>().Update(job);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
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
        InvalidateListCaches();
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
        InvalidateListCaches();
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

        InvalidateListCaches();
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

        InvalidateListCaches();
    }

    public async Task<Guid> AddLaborAsync(JobLabor labor, CancellationToken ct = default)
    {
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

        InvalidateListCaches();
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

        InvalidateListCaches();
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory("jobs");

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
}
