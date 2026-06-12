using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly AppDbContext _dbContext;
    private readonly IJobService _jobService;

    public SalesOrderService(AppDbContext dbContext, IJobService jobService)
    {
        _dbContext = dbContext;
        _jobService = jobService;
    }

    public async Task<SalesOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<SalesOrder>()
            .Include(so => so.Lines)
            .Include(so => so.Customer)
            .Include(so => so.Quote)
            .FirstOrDefaultAsync(so => so.Id == id, ct);
    }

    public async Task<IReadOnlyList<SalesOrder>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _dbContext.Set<SalesOrder>()
            .AsNoTracking()
            .Include(so => so.Customer)
            .Include(so => so.Quote)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(so =>
                so.SoNumber.ToLower().Contains(term) ||
                (so.Notes != null && so.Notes.ToLower().Contains(term)) ||
                (so.Customer != null && so.Customer.Name.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(so => so.SoDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(SalesOrder so, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(so.SoNumber))
        {
            so.SoNumber = $"SO-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        RecalculateTotals(so);

        _dbContext.Set<SalesOrder>().Add(so);
        await _dbContext.SaveChangesAsync(ct);
        return so.Id;
    }

    public async Task UpdateAsync(SalesOrder so, CancellationToken ct = default)
    {
        RecalculateTotals(so);
        _dbContext.Set<SalesOrder>().Update(so);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (so == null) return;

        foreach (var line in so.Lines)
        {
            line.IsDeleted = true;
        }
        so.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid soId, SalesOrderStatus newStatus, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>().FirstOrDefaultAsync(s => s.Id == soId, ct);
        if (so == null) return;

        so.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<Guid> AddLineAsync(SalesOrderLine line, CancellationToken ct = default)
    {
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<SalesOrderLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == line.SalesOrderId, ct);
        if (so != null)
        {
            RecalculateTotals(so);
            await _dbContext.SaveChangesAsync(ct);
        }

        return line.Id;
    }

    public async Task UpdateLineAsync(SalesOrderLine line, CancellationToken ct = default)
    {
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<SalesOrderLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == line.SalesOrderId, ct);
        if (so != null)
        {
            RecalculateTotals(so);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<SalesOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var soId = line.SalesOrderId;
        line.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == soId, ct);
        if (so != null)
        {
            RecalculateTotals(so);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<Job> ConvertToJobAsync(Guid soId, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .Include(s => s.Customer)
            .Include(s => s.Quote)
            .FirstOrDefaultAsync(s => s.Id == soId, ct);

        if (so == null)
            throw new InvalidOperationException("Sales Order not found.");

        if (so.Status != SalesOrderStatus.Confirmed && so.Status != SalesOrderStatus.InProgress)
        {
            so.Status = SalesOrderStatus.Confirmed;
        }

        var job = new Job
        {
            QuoteId = so.QuoteId,
            SalesOrderId = so.Id,
            CustomerId = so.CustomerId,
            JobNumber = $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}",
            Title = $"Job from {so.SoNumber}",
            Description = so.Notes,
            QuotedTotal = so.Total,
            ActualCost = 0,
            ScheduledStart = so.DeliveryDate ?? DateTime.UtcNow.AddDays(7),
            Status = JobStatus.Scheduled
        };

        if (so.Customer != null)
        {
            job.Title = $"{so.Customer.Name} - {so.SoNumber}";
        }

        _dbContext.Set<Job>().Add(job);

        await _dbContext.SaveChangesAsync(ct);

        // Update SO status
        so.Status = SalesOrderStatus.InProgress;
        await _dbContext.SaveChangesAsync(ct);

        // Return loaded job (reuse from JobService or simple)
        return (await _jobService.GetByIdAsync(job.Id, ct))!;
    }

    private static void RecalculateTotals(SalesOrder so)
    {
        so.Subtotal = so.Lines
            .Where(l => !l.IsDeleted)
            .Sum(l => l.LineTotal);

        so.Tax = Math.Round(so.Subtotal * so.TaxRate, 2);
        so.Total = so.Subtotal + so.Tax;
    }
}
