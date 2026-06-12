using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class QuoteService : IQuoteService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;

    public QuoteService(AppDbContext dbContext, ITenantService? tenantService = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
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
        var query = _dbContext.Set<Quote>()
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

        return await query
            .OrderByDescending(q => q.QuoteDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Quote quote, CancellationToken ct = default)
    {
        // Generate a simple quote number if not provided
        if (string.IsNullOrWhiteSpace(quote.QuoteNumber))
        {
            quote.QuoteNumber = $"Q-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        quote.RecalculateTotals();

        _dbContext.Set<Quote>().Add(quote);
        await _dbContext.SaveChangesAsync(ct);

        // Commercial usage tracking
        _ = Task.Run(async () =>
        {
            try
            {
                var tid = quote.TenantId; // stamped by DbContext
                if (tid != Guid.Empty && _tenantService != null)
                    await _tenantService.IncrementQuoteCountAsync(tid);
            }
            catch { /* ignore */ }
        });

        return quote.Id;
    }

    public async Task UpdateAsync(Quote quote, CancellationToken ct = default)
    {
        quote.RecalculateTotals();
        _dbContext.Set<Quote>().Update(quote);
        await _dbContext.SaveChangesAsync(ct);
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
    }

    public async Task<Job> ConvertToJobAsync(Guid quoteId, CancellationToken ct = default)
    {
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

        // Optionally seed some initial actual cost placeholders from the quote lines (commented for clean start)
        // foreach (var line in quote.Lines)
        // {
        //     _dbContext.Set<JobCost>().Add(new JobCost
        //     {
        //         JobId = job.Id,
        //         Description = line.Description,
        //         Amount = line.LineTotal,
        //         CostType = line.LineType
        //     });
        // }

        await _dbContext.SaveChangesAsync(ct);

        // Return loaded job
        return (await GetByIdForJobAsync(job.Id, ct))!;
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
