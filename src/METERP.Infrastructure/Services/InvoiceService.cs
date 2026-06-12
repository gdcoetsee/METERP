using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class InvoiceService : IInvoiceService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;
    private readonly IInvoiceIntegrationService? _invoiceIntegration;
    private readonly ITenantCacheService? _cache;

    public InvoiceService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        IInvoiceIntegrationService? invoiceIntegration = null,
        ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _invoiceIntegration = invoiceIntegration;
        _cache = cache;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Invoice>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                "invoices",
                $"p{page}:s{pageSize}",
                () => LoadInvoicesAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadInvoicesAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Invoice>> LoadInvoicesAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i =>
                i.InvoiceNumber.ToLower().Contains(term) ||
                (i.Notes != null && i.Notes.ToLower().Contains(term)) ||
                (i.Customer != null && i.Customer.Name.ToLower().Contains(term)) ||
                (i.Job != null && i.Job.JobNumber.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(i => i.InvoiceDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Invoice invoice, CancellationToken ct = default)
    {
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? invoice.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Invoice, ct);

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            invoice.InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        invoice.RecalculateTotals();

        _dbContext.Set<Invoice>().Add(invoice);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementInvoiceCountAsync(invoice.TenantId, invoice.Total, ct);
        await TryNotifyInvoiceCreatedAsync(invoice.Id, ct);
        InvalidateListCaches();

        return invoice.Id;
    }

    public async Task UpdateAsync(Invoice invoice, CancellationToken ct = default)
    {
        invoice.RecalculateTotals();
        _dbContext.Set<Invoice>().Update(invoice);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (invoice == null) return;

        foreach (var line in invoice.Lines)
        {
            line.IsDeleted = true;
        }
        invoice.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task<Guid> AddLineAsync(InvoiceLine line, CancellationToken ct = default)
    {
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<InvoiceLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == line.InvoiceId, ct);
        if (invoice != null)
        {
            invoice.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(InvoiceLine line, CancellationToken ct = default)
    {
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<InvoiceLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == line.InvoiceId, ct);
        if (invoice != null)
        {
            invoice.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<InvoiceLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var invoiceId = line.InvoiceId;
        line.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice != null)
        {
            invoice.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
    }

    public async Task<Invoice> CreateFromJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>()
            .Include(j => j.Customer)
            .Include(j => j.Quote)
                .ThenInclude(q => q != null ? q.Lines : null)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
            throw new InvalidOperationException("Job not found.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? job.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Invoice, ct);

        var invoice = new Invoice
        {
            CustomerId = job.CustomerId,
            JobId = job.Id,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30),
            Status = InvoiceStatus.Draft,
            Notes = job.Description ?? job.Notes,
            TaxRate = 0.15m
        };

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            invoice.InvoiceNumber = $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        _dbContext.Set<Invoice>().Add(invoice);

        // Prefer lines from originating quote if available
        bool linesAdded = false;
        if (job.Quote != null && job.Quote.Lines != null && job.Quote.Lines.Any(l => !l.IsDeleted))
        {
            foreach (var ql in job.Quote.Lines.Where(l => !l.IsDeleted))
            {
                _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
                {
                    InvoiceId = invoice.Id,
                    Description = ql.Description,
                    Quantity = ql.Quantity,
                    UnitPrice = ql.UnitPrice,
                    Unit = ql.Unit,
                    LineType = ql.LineType
                    // LineTotal is computed as Quantity * UnitPrice
                });
            }
            linesAdded = true;
        }

        if (!linesAdded)
        {
            // Fallback: single summary line based on quoted total
            _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                Description = $"Work per Job {job.JobNumber}",
                Quantity = 1,
                UnitPrice = job.QuotedTotal,
                LineType = "Other"
                // LineTotal will compute to QuotedTotal
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        // Recalculate after lines are persisted (to have final total for revenue)
        var saved = await GetByIdAsync(invoice.Id, ct);
        if (saved != null)
        {
            saved.RecalculateTotals();
            await _dbContext.SaveChangesAsync(ct);

            await TryIncrementInvoiceCountAsync(saved.TenantId, saved.Total, ct);
            await TryNotifyInvoiceCreatedAsync(saved.Id, ct);
            InvalidateListCaches();

            return saved;
        }

        return invoice;
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory("invoices");

    private async Task TryNotifyInvoiceCreatedAsync(Guid invoiceId, CancellationToken ct)
    {
        if (_invoiceIntegration == null) return;
        try
        {
            await _invoiceIntegration.NotifyInvoiceCreatedAsync(invoiceId, ct);
        }
        catch
        {
            // Best-effort integrations — must not break invoicing.
        }
    }

    private async Task TryIncrementInvoiceCountAsync(Guid tenantId, decimal revenue, CancellationToken ct)
    {
        if (tenantId == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementInvoiceCountAsync(tenantId, revenue, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break business operations.
        }
    }

    public async Task UpdateStatusAsync(Guid invoiceId, InvoiceStatus newStatus, CancellationToken ct = default)
    {
        var invoice = await _dbContext.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null) return;

        invoice.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }
}
