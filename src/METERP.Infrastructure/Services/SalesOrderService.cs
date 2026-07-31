using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly AppDbContext _dbContext;
    private readonly IJobService _jobService;
    private readonly ITenantService? _tenantService;
    private readonly ITenantCacheService? _cache;

    public SalesOrderService(
        AppDbContext dbContext,
        IJobService jobService,
        ITenantService? tenantService = null,
        ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _jobService = jobService;
        _tenantService = tenantService;
        _cache = cache;
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
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.SalesOrders,
                $"p{page}:s{pageSize}",
                () => LoadSalesOrdersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadSalesOrdersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<SalesOrder>> LoadSalesOrdersAsync(string? search, int page, int pageSize, CancellationToken ct)
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
        if (so.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for a sales order.");

        var customer = await _dbContext.Set<Customer>().FindAsync([so.CustomerId], ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found.");

        await ValidateQuoteLinkAsync(so.QuoteId, so.CustomerId, ct);

        if (so.TaxRate < 0 || so.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");

        if (so.DeliveryDate.HasValue && so.SoDate != default
            && so.DeliveryDate.Value.Date < so.SoDate.Date)
            throw new InvalidOperationException("Delivery date cannot be before the sales order date.");
        if (so.DeliveryDate.HasValue && so.DeliveryDate.Value.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Delivery date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(so.Notes))
        {
            so.Notes = so.Notes.Trim();
            if (so.Notes.Length > 2000)
                throw new InvalidOperationException("Sales order notes cannot exceed 2000 characters.");
        }

        if (string.IsNullOrWhiteSpace(so.SoNumber))
        {
            so.SoNumber = $"SO-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }
        else
        {
            so.SoNumber = so.SoNumber.Trim();
            if (so.SoNumber.Length > 50)
                throw new InvalidOperationException("Sales order number cannot exceed 50 characters.");
            var numberTaken = await _dbContext.Set<SalesOrder>()
                .AnyAsync(s => s.SoNumber == so.SoNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Sales order number '{so.SoNumber}' already exists.");
        }

        RecalculateTotals(so);

        _dbContext.Set<SalesOrder>().Add(so);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return so.Id;
    }

    public async Task UpdateAsync(SalesOrder so, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<SalesOrder>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == so.Id, ct);
        if (existing == null)
            throw new InvalidOperationException("Sales order not found.");
        if (existing.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
            throw new InvalidOperationException(
                $"Cannot edit sales order in status {existing.Status}.");

        if (so.TaxRate < 0 || so.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");
        if (so.DeliveryDate.HasValue && so.SoDate != default
            && so.DeliveryDate.Value.Date < so.SoDate.Date)
            throw new InvalidOperationException("Delivery date cannot be before the sales order date.");
        if (so.DeliveryDate.HasValue && so.DeliveryDate.Value.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Delivery date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(so.Notes))
        {
            so.Notes = so.Notes.Trim();
            if (so.Notes.Length > 2000)
                throw new InvalidOperationException("Sales order notes cannot exceed 2000 characters.");
        }

        if (so.CustomerId == Guid.Empty)
            so.CustomerId = existing.CustomerId;
        else if (so.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>().FindAsync([so.CustomerId], ct);
            if (customer == null || customer.IsDeleted)
                throw new InvalidOperationException("Customer not found.");
        }

        // Quote link is set at create; free-form updates cannot re-point the SO.
        so.QuoteId = existing.QuoteId;
        so.SoNumber = existing.SoNumber;
        so.Status = existing.Status;

        RecalculateTotals(so);
        _dbContext.Set<SalesOrder>().Update(so);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (so == null) return;

        if (so.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Cancelled))
            throw new InvalidOperationException(
                $"Cannot delete sales order in status {so.Status}. Cancel or keep draft only.");

        var linkedJob = await _dbContext.Set<Job>().AsNoTracking()
            .AnyAsync(j => j.SalesOrderId == so.Id, ct);
        if (linkedJob)
            throw new InvalidOperationException(
                $"Cannot delete sales order {so.SoNumber} — it is linked to a job.");

        foreach (var line in so.Lines)
        {
            line.IsDeleted = true;
        }
        so.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task UpdateStatusAsync(Guid soId, SalesOrderStatus newStatus, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == soId, ct);
        if (so == null) return;
        if (so.Status == newStatus) return;

        if (so.Status == SalesOrderStatus.Cancelled)
            throw new InvalidOperationException("Cancelled sales orders cannot change status.");

        if (so.Status == SalesOrderStatus.Completed && newStatus != SalesOrderStatus.Completed)
            throw new InvalidOperationException("Completed sales orders cannot change status.");

        if (newStatus == SalesOrderStatus.Confirmed
            && so.Status == SalesOrderStatus.Draft
            && !so.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot confirm a sales order with no lines.");

        if (newStatus == SalesOrderStatus.Cancelled && so.Status == SalesOrderStatus.InProgress)
        {
            var hasJob = await _dbContext.Set<Job>().AsNoTracking()
                .AnyAsync(j => j.SalesOrderId == so.Id, ct);
            if (hasJob)
                throw new InvalidOperationException(
                    "Cannot cancel a sales order that already has a job. Cancel the job instead.");
        }

        so.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task<Guid> AddLineAsync(SalesOrderLine line, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == line.SalesOrderId, ct)
            ?? throw new InvalidOperationException("Sales order not found.");

        if (so.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
            throw new InvalidOperationException("Lines can only be added to draft or confirmed sales orders.");

        ValidateLine(line);

        _dbContext.Set<SalesOrderLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        await _dbContext.Entry(so).Collection(s => s.Lines).LoadAsync(ct);
        RecalculateTotals(so);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(SalesOrderLine line, CancellationToken ct = default)
    {
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == line.SalesOrderId, ct)
            ?? throw new InvalidOperationException("Sales order not found.");

        if (so.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
            throw new InvalidOperationException("Lines can only be edited on draft or confirmed sales orders.");

        ValidateLine(line);

        _dbContext.Set<SalesOrderLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        RecalculateTotals(so);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<SalesOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var soId = line.SalesOrderId;
        var so = await _dbContext.Set<SalesOrder>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == soId, ct);
        if (so == null) return;

        if (so.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
            throw new InvalidOperationException("Lines can only be removed from draft or confirmed sales orders.");

        line.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);

        RecalculateTotals(so);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
    }

    public async Task<Job> ConvertToJobAsync(Guid soId, CancellationToken ct = default)
    {
        var tenantId = _dbContext.CurrentTenantId;
        // IgnoreQueryFilters so soft-deleted customers still yield a clear conversion error.
        var so = await _dbContext.Set<SalesOrder>()
            .IgnoreQueryFilters()
            .Include(s => s.Lines)
            .Include(s => s.Customer)
            .Include(s => s.Quote)
            .FirstOrDefaultAsync(s =>
                s.Id == soId
                && !s.IsDeleted
                && (tenantId == Guid.Empty || s.TenantId == tenantId), ct);

        if (so == null)
            throw new InvalidOperationException("Sales Order not found.");

        // Prevent double conversion.
        var existingJob = await _dbContext.Set<Job>()
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.SalesOrderId == so.Id, ct);
        if (existingJob != null)
            throw new InvalidOperationException($"Sales order {so.SoNumber} already converted to job {existingJob.JobNumber}.");

        if (so.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Completed)
            throw new InvalidOperationException($"Cannot convert sales order in status {so.Status}.");

        if (!so.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot convert a sales order with no lines to a job.");

        if (so.Customer == null || so.Customer.IsDeleted)
            throw new InvalidOperationException("Cannot convert a sales order whose customer is missing or deleted.");

        if (so.Status != SalesOrderStatus.Confirmed && so.Status != SalesOrderStatus.InProgress)
            so.Status = SalesOrderStatus.Confirmed;

        var title = $"{so.Customer.Name} - {so.SoNumber}";

        // Route through JobService so quota enforcement and usage counters apply.
        var jobId = await _jobService.CreateAsync(new Job
        {
            QuoteId = so.QuoteId,
            SalesOrderId = so.Id,
            CustomerId = so.CustomerId,
            Title = title,
            Description = so.Notes,
            QuotedTotal = so.Total,
            ActualCost = 0,
            ScheduledStart = so.DeliveryDate ?? DateTime.UtcNow.AddDays(7),
            Status = JobStatus.Scheduled
        }, ct);

        so.Status = SalesOrderStatus.InProgress;
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
        if (_cache != null)
            await TenantCacheInvalidation.OnJobMutatedAsync(_cache, ct);

        return (await _jobService.GetByIdAsync(jobId, ct))!;
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.SalesOrders);

    private async Task ValidateQuoteLinkAsync(Guid quoteId, Guid customerId, CancellationToken ct)
    {
        if (quoteId == Guid.Empty)
            throw new InvalidOperationException("Quote is required for a sales order.");

        var quote = await _dbContext.Set<Quote>().AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote == null)
            throw new InvalidOperationException("Linked quote not found.");

        if (quote.CustomerId != customerId)
            throw new InvalidOperationException(
                "Sales order customer must match the linked quote's customer.");

        if (quote.Status is QuoteStatus.Rejected or QuoteStatus.Expired)
            throw new InvalidOperationException(
                $"Cannot create a sales order from a {quote.Status.ToString().ToLowerInvariant()} quote.");
    }

    private static void ValidateLine(SalesOrderLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Description))
            throw new InvalidOperationException("Line description is required.");
        if (line.Quantity <= 0)
            throw new InvalidOperationException("Line quantity must be positive.");
        if (line.Quantity > 1_000_000m)
            throw new InvalidOperationException("Line quantity cannot exceed 1,000,000.");
        if (line.UnitPrice < 0)
            throw new InvalidOperationException("Line unit price cannot be negative.");
        if (line.UnitPrice > 10_000_000m)
            throw new InvalidOperationException("Line unit price cannot exceed 10,000,000.");

        line.Description = line.Description.Trim();
        if (line.Description.Length > 500)
            throw new InvalidOperationException("Line description cannot exceed 500 characters.");
        if (!string.IsNullOrWhiteSpace(line.Unit))
        {
            line.Unit = line.Unit.Trim();
            if (line.Unit.Length > 20)
                throw new InvalidOperationException("Line unit cannot exceed 20 characters.");
        }
        if (!string.IsNullOrWhiteSpace(line.LineType))
        {
            line.LineType = line.LineType.Trim();
            if (line.LineType.Length > 50)
                throw new InvalidOperationException("Line type cannot exceed 50 characters.");
        }
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
