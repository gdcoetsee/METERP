using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class QuoteService : IQuoteService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;
    private readonly ITenantCacheService? _cache;
    private readonly IAuditService? _auditService;

    public QuoteService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        ITenantCacheService? cache = null,
        IAuditService? auditService = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _cache = cache;
        _auditService = auditService;
    }

    public async Task<Quote?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }

    public async Task<IReadOnlyList<Quote>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                "quotes",
                $"p{page}:s{pageSize}",
                () => LoadQuotesAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadQuotesAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Quote>> LoadQuotesAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(q =>
                q.QuoteNumber.ToLower().Contains(term) ||
                (q.Notes != null && q.Notes.ToLower().Contains(term)) ||
                (q.Customer != null && q.Customer.Name.ToLower().Contains(term)));
        }

        var results = await query
            .OrderByDescending(q => q.QuoteDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        ListCacheGraphHelper.PrepareQuotesForCache(results);
        return results;
    }

    public async Task<Guid> CreateAsync(Quote quote, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? quote.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Quote, ct);

        // Generate a simple quote number if not provided
        if (string.IsNullOrWhiteSpace(quote.QuoteNumber))
        {
            quote.QuoteNumber = $"Q-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        quote.RecalculateTotals();

        _dbContext.Set<Quote>().Add(quote);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementQuoteCountAsync(quote.TenantId, ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CREATE",
                "Quote",
                quote.QuoteNumber,
                $"Customer {quote.CustomerId}, total R {quote.Total:N2}",
                ct);
        }

        return quote.Id;
    }

    public async Task UpdateAsync(Quote quote, CancellationToken ct = default)
    {
        quote.RecalculateTotals();
        _dbContext.Set<Quote>().Update(quote);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (quote == null) return;

        foreach (var line in quote.Lines)
        {
            line.IsDeleted = true;
        }
        quote.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task<Guid> AddLineAsync(QuoteLine line, CancellationToken ct = default)
    {
        _dbContext.Set<QuoteLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        // Recalculate parent (LineTotal is now computed on the entity)
        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == line.QuoteId, ct);
        if (quote != null)
        {
            quote.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(QuoteLine line, CancellationToken ct = default)
    {
        _dbContext.Set<QuoteLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == line.QuoteId, ct);
        if (quote != null)
        {
            quote.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<QuoteLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var quoteId = line.QuoteId;
        line.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote != null)
        {
            quote.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
    }

    public async Task<Job> ConvertToJobAsync(Guid quoteId, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Job, ct);

        var quote = await _dbContext.Set<Quote>()
            .Include(q => q.Lines)
            .Include(q => q.Customer)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);

        if (quote == null)
            throw new InvalidOperationException("Quote not found.");

        if (quote.Status != QuoteStatus.Accepted)
        {
            quote.Status = QuoteStatus.Accepted;
        }

        var job = new Job
        {
            QuoteId = quote.Id,
            CustomerId = quote.CustomerId,
            JobNumber = $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}",
            Title = $"Job from {quote.QuoteNumber}",
            Description = quote.Notes,
            QuotedTotal = quote.Total,
            ActualCost = 0,
            ScheduledStart = DateTime.UtcNow.AddDays(7),
            Status = JobStatus.Scheduled
        };

        if (quote.Customer != null)
        {
            job.Title = $"{quote.Customer.Name} - {quote.QuoteNumber}";
        }

        _dbContext.Set<Job>().Add(job);
        await _dbContext.SaveChangesAsync(ct);

        // Explicit travel from quote lines — contractor differentiator; carried into job costing.
        foreach (var line in quote.Lines.Where(l => !l.IsDeleted))
        {
            var isTravel = line.Description.Contains("Travel", StringComparison.OrdinalIgnoreCase)
                || string.Equals(line.LineType, "Travel", StringComparison.OrdinalIgnoreCase);
            if (!isTravel) continue;

            _dbContext.Set<JobCost>().Add(new JobCost
            {
                JobId = job.Id,
                Description = line.Description,
                Amount = line.LineTotal,
                CostType = "Travel",
                CostDate = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
        _cache?.InvalidateCategory("jobs");

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CONVERT",
                "Quote",
                quote.QuoteNumber,
                $"Converted to job {job.JobNumber} with explicit travel costs",
                ct);
        }

        return (await GetByIdForJobAsync(job.Id, ct))!;
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory("quotes");

    private async Task TryIncrementQuoteCountAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementQuoteCountAsync(tenantId, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break business operations.
        }
    }

    private async Task<Job?> GetByIdForJobAsync(Guid id, CancellationToken ct)
    {
        return await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Customer)
            .Include(j => j.Quote)
                .ThenInclude(q => q!.Lines)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
    }
}
