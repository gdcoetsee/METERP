using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
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
    private readonly IAuditService? _auditService;
    private readonly IDocumentSequenceService? _documentSequence;
    private readonly IDocumentStorageService? _documentStorage;

    public InvoiceService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        IInvoiceIntegrationService? invoiceIntegration = null,
        ITenantCacheService? cache = null,
        IAuditService? auditService = null,
        IDocumentSequenceService? documentSequence = null,
        IDocumentStorageService? documentStorage = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _invoiceIntegration = invoiceIntegration;
        _cache = cache;
        _auditService = auditService;
        _documentSequence = documentSequence;
        _documentStorage = documentStorage;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Invoice>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Invoices,
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
            invoice.InvoiceNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Invoice", "INV", ct)
                : $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
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

    public Task<Invoice> CreateFromJobAsync(Guid jobId, CancellationToken ct = default) =>
        CreateBillingDocumentAsync(jobId, InvoiceDocumentType.Final, null, ct);

    public async Task<Invoice> CreateBillingDocumentAsync(
        Guid jobId,
        InvoiceDocumentType documentType,
        decimal? percentOfQuotedTotal = null,
        CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>()
            .Include(j => j.Customer)
            .Include(j => j.Quote)
                .ThenInclude(q => q != null ? q.Lines : null)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
            throw new InvalidOperationException("Job not found.");

        if (!job.IsOpenForOperations())
            throw JobClosedException.ForJob(job.JobNumber);

        if (documentType is InvoiceDocumentType.Final or InvoiceDocumentType.Partial or InvoiceDocumentType.Standard)
        {
            if (job.SignOffStatus != JobSignOffStatus.SignedOff)
            {
                throw new InvalidOperationException(
                    "Job requires client sign-off before final or partial invoicing. Record sign-off on the job first.");
            }
        }

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? job.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty && documentType != InvoiceDocumentType.Proforma)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Invoice, ct);

        var (sequenceType, prefix) = GetSequenceForDocumentType(documentType);
        var invoice = new Invoice
        {
            CustomerId = job.CustomerId,
            JobId = job.Id,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30),
            Status = InvoiceStatus.Draft,
            DocumentType = documentType,
            Notes = job.Description ?? job.Notes,
            TaxRate = 0.15m,
            RetentionPercent = documentType is InvoiceDocumentType.Final or InvoiceDocumentType.Partial
                ? job.RetentionPercent
                : 0m
        };

        invoice.InvoiceNumber = _documentSequence != null
            ? await _documentSequence.GetNextNumberAsync(sequenceType, prefix, ct)
            : $"{prefix}-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        _dbContext.Set<Invoice>().Add(invoice);
        AddLinesForBillingDocument(invoice, job, documentType, percentOfQuotedTotal);
        await _dbContext.SaveChangesAsync(ct);

        var saved = await GetByIdAsync(invoice.Id, ct);
        if (saved == null)
            return invoice;

        saved.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        if (documentType != InvoiceDocumentType.Proforma)
        {
            await TryIncrementInvoiceCountAsync(saved.TenantId, saved.Total, ct);
            await TryNotifyInvoiceCreatedAsync(saved.Id, ct);
        }

        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CREATE",
                "Invoice",
                saved.InvoiceNumber,
                $"Created {documentType} from job {job.JobNumber}, total R {saved.Total:N0}",
                ct);
        }

        return saved;
    }

    public async Task<Invoice> CreateCreditNoteAsync(Guid sourceInvoiceId, string reason, CancellationToken ct = default)
    {
        var source = await GetByIdAsync(sourceInvoiceId, ct);
        if (source == null)
            throw new InvalidOperationException("Source invoice not found.");

        if (source.DocumentType == InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Cannot create a credit note from another credit note.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? source.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Invoice, ct);

        var creditNote = new Invoice
        {
            CustomerId = source.CustomerId,
            JobId = source.JobId,
            InvoiceDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow,
            Status = InvoiceStatus.Draft,
            DocumentType = InvoiceDocumentType.CreditNote,
            CreditNoteForInvoiceId = source.Id,
            TaxRate = source.TaxRate,
            Notes = reason
        };

        creditNote.InvoiceNumber = _documentSequence != null
            ? await _documentSequence.GetNextNumberAsync("CreditNote", "CN", ct)
            : $"CN-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        _dbContext.Set<Invoice>().Add(creditNote);

        foreach (var line in source.Lines.Where(l => !l.IsDeleted))
        {
            _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
            {
                InvoiceId = creditNote.Id,
                Description = $"Credit: {line.Description}",
                Quantity = line.Quantity,
                UnitPrice = -line.UnitPrice,
                Unit = line.Unit,
                LineType = line.LineType
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        var saved = await GetByIdAsync(creditNote.Id, ct);
        if (saved == null)
            return creditNote;

        saved.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "CREATE",
                "Invoice",
                saved.InvoiceNumber,
                $"Credit note for {source.InvoiceNumber}: {reason}",
                ct);
        }

        return saved;
    }

    public async Task<IReadOnlyList<InvoicePayment>> GetPaymentsAsync(Guid invoiceId, CancellationToken ct = default)
    {
        return await _dbContext.Set<InvoicePayment>()
            .AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync(ct);
    }

    public Task<Guid> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        DateTime paymentDate,
        string? reference,
        Guid? recordedByUserId,
        string? notes,
        CancellationToken ct = default) =>
        RecordPaymentInternalAsync(invoiceId, amount, paymentDate, reference, recordedByUserId, notes, null, null, null, ct);

    public async Task<Guid> RecordPaymentWithPopAsync(
        Guid invoiceId,
        decimal amount,
        DateTime paymentDate,
        string? reference,
        string fileName,
        Stream popContent,
        string contentType,
        Guid? recordedByUserId,
        string? notes,
        CancellationToken ct = default)
    {
        if (_documentStorage == null)
            throw new InvalidOperationException("Document storage is not configured.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        var stored = await _documentStorage.SaveAsync(tenantId, "invoice-pop", fileName, popContent, contentType, ct);

        return await RecordPaymentInternalAsync(
            invoiceId,
            amount,
            paymentDate,
            reference,
            recordedByUserId,
            notes,
            stored.StorageKey,
            stored.FileName,
            stored.ContentType,
            ct);
    }

    public async Task<IReadOnlyList<AgedDebtorRow>> GetAgedDebtorsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.DocumentType != InvoiceDocumentType.Proforma
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Draft)
            .ToListAsync(ct);

        return invoices
            .Select(i =>
            {
                var balance = InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid);
                var days = InvoiceBillingCalculator.GetDaysOverdue(i.DueDate, now);
                return new AgedDebtorRow(
                    i.Id,
                    i.InvoiceNumber,
                    i.Customer?.Name ?? "—",
                    i.DueDate,
                    i.Total,
                    i.AmountPaid,
                    balance,
                    days,
                    InvoiceBillingCalculator.GetAgingBucket(days));
            })
            .Where(r => r.BalanceDue > 0)
            .OrderByDescending(r => r.DaysOverdue)
            .ToList();
    }

    private static (string SequenceType, string Prefix) GetSequenceForDocumentType(InvoiceDocumentType type) => type switch
    {
        InvoiceDocumentType.Proforma => ("Proforma", "PRO"),
        InvoiceDocumentType.Deposit => ("Deposit", "DEP"),
        InvoiceDocumentType.Partial => ("PartialInvoice", "PINV"),
        InvoiceDocumentType.CreditNote => ("CreditNote", "CN"),
        _ => ("Invoice", "INV")
    };

    private void AddLinesForBillingDocument(
        Invoice invoice,
        Job job,
        InvoiceDocumentType documentType,
        decimal? percentOfQuotedTotal)
    {
        switch (documentType)
        {
            case InvoiceDocumentType.Deposit:
            {
                var pct = percentOfQuotedTotal ?? job.DepositPercent;
                var amount = Math.Round(job.QuotedTotal * pct / 100m, 2);
                _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
                {
                    InvoiceId = invoice.Id,
                    Description = $"Deposit ({pct:N0}% of quoted work) — Job {job.JobNumber}",
                    Quantity = 1,
                    UnitPrice = amount,
                    LineType = "Other"
                });
                break;
            }
            case InvoiceDocumentType.Proforma:
            case InvoiceDocumentType.Partial:
            {
                if (percentOfQuotedTotal is > 0 and <= 100)
                {
                    var amount = Math.Round(job.QuotedTotal * percentOfQuotedTotal.Value / 100m, 2);
                    _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
                    {
                        InvoiceId = invoice.Id,
                        Description = $"{documentType} ({percentOfQuotedTotal:N0}%) — Job {job.JobNumber}",
                        Quantity = 1,
                        UnitPrice = amount,
                        LineType = "Other"
                    });
                }
                else
                {
                    AddQuoteOrSummaryLines(invoice, job);
                }

                break;
            }
            default:
                AddQuoteOrSummaryLines(invoice, job);
                break;
        }
    }

    private void AddQuoteOrSummaryLines(Invoice invoice, Job job)
    {
        var linesAdded = false;
        if (job.Quote?.Lines != null && job.Quote.Lines.Any(l => !l.IsDeleted))
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
                });
            }

            linesAdded = true;
        }

        if (!linesAdded)
        {
            _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                Description = $"Work per Job {job.JobNumber}",
                Quantity = 1,
                UnitPrice = job.QuotedTotal,
                LineType = "Other"
            });
        }
    }

    private async Task<Guid> RecordPaymentInternalAsync(
        Guid invoiceId,
        decimal amount,
        DateTime paymentDate,
        string? reference,
        Guid? recordedByUserId,
        string? notes,
        string? popStorageKey,
        string? popFileName,
        string? popContentType,
        CancellationToken ct)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Payment amount must be positive.");

        var invoice = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice == null)
            throw new InvalidOperationException("Invoice not found.");

        if (invoice.DocumentType == InvoiceDocumentType.Proforma)
            throw new InvalidOperationException("Payments cannot be recorded against proforma invoices.");

        var payment = new InvoicePayment
        {
            InvoiceId = invoiceId,
            Amount = amount,
            PaymentDate = paymentDate,
            Reference = reference,
            RecordedByUserId = recordedByUserId,
            Notes = notes,
            PopStorageKey = popStorageKey,
            PopFileName = popFileName,
            PopContentType = popContentType
        };

        _dbContext.Set<InvoicePayment>().Add(payment);

        invoice.AmountPaid = Math.Round(invoice.AmountPaid + amount, 2);
        invoice.Status = InvoiceBillingCalculator.DerivePaymentStatus(
            invoice.Total,
            invoice.AmountPaid,
            invoice.Status,
            invoice.DueDate,
            DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "PAYMENT",
                "Invoice",
                invoice.InvoiceNumber,
                $"Recorded payment R {amount:N2}" + (reference != null ? $" ref {reference}" : ""),
                ct);
        }

        return payment.Id;
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.Invoices);

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
