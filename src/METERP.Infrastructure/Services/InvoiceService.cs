using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Models;
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
    private readonly IEmailSender? _email;
    private readonly ITenantNotificationService? _notifications;

    public InvoiceService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        IInvoiceIntegrationService? invoiceIntegration = null,
        ITenantCacheService? cache = null,
        IAuditService? auditService = null,
        IDocumentSequenceService? documentSequence = null,
        IDocumentStorageService? documentStorage = null,
        IEmailSender? email = null,
        ITenantNotificationService? notifications = null)
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
        _email = email;
        _notifications = notifications;
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .Include(i => i.Payments)
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .Include(i => i.CreditNoteForInvoice)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Invoice>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default,
        bool unlinkedOnly = false,
        bool unlinkedCreditsOnly = false)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search) && !unlinkedOnly && !unlinkedCreditsOnly)
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Invoices,
                $"p{page}:s{pageSize}",
                () => LoadInvoicesAsync(search, page, pageSize, false, false, ct),
                ct: ct);
        }

        return await LoadInvoicesAsync(search, page, pageSize, unlinkedOnly, unlinkedCreditsOnly, ct);
    }

    public async Task LinkCreditNoteParentAsync(Guid creditNoteId, string parentInvoiceNumber, CancellationToken ct = default)
    {
        var number = parentInvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(number))
            throw new InvalidOperationException("Enter the parent invoice number.");

        var credit = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == creditNoteId, ct)
            ?? throw new InvalidOperationException("Credit note not found.");

        if (credit.DocumentType != InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Only a credit note can be linked to a parent invoice.");

        var parent = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.InvoiceNumber.ToLower() == number.ToLower(), ct)
            ?? throw new InvalidOperationException($"Invoice '{number}' was not found.");

        if (parent.Id == credit.Id)
            throw new InvalidOperationException("A credit note cannot be its own parent.");

        if (parent.DocumentType is InvoiceDocumentType.CreditNote or InvoiceDocumentType.Proforma)
            throw new InvalidOperationException("Link the credit note to a sales invoice, not a credit note or proforma.");

        if (parent.CustomerId != credit.CustomerId)
            throw new InvalidOperationException("The parent invoice must belong to the same customer.");

        if (credit.CreditNoteForInvoiceId is Guid existing)
        {
            if (existing == parent.Id)
                return;

            throw new InvalidOperationException("This credit note is already linked to another invoice.");
        }

        credit.CreditNoteForInvoiceId = parent.Id;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "LINK_CREDIT",
                "Invoice",
                credit.InvoiceNumber,
                $"Linked credit note {credit.InvoiceNumber} to {parent.InvoiceNumber}. Totals were not changed.",
                ct);
        }
    }

    public async Task LinkJobAsync(Guid invoiceId, Guid jobId, CancellationToken ct = default)
    {
        if (jobId == Guid.Empty)
            throw new InvalidOperationException("Choose a job to link.");

        var invoice = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.JobId is Guid existing && existing != Guid.Empty)
        {
            if (existing == jobId)
                return;
            throw new InvalidOperationException("Invoice is already linked to a job.");
        }

        await ValidateInvoiceJobLinkAsync(jobId, invoice.CustomerId, ct);

        var jobNumber = await _dbContext.Set<Job>().AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.JobNumber)
            .FirstAsync(ct);

        // JobId only. Totals, status, and the job itself stay as they are — linking is not a close.
        invoice.JobId = jobId;
        await _dbContext.SaveChangesAsync(ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "LINK_JOB",
                "Invoice",
                invoice.InvoiceNumber,
                $"Linked {invoice.InvoiceNumber} to {jobNumber}. Invoice totals and job status were not changed.",
                ct);
        }

        InvalidateListCaches();
    }

    private async Task<IReadOnlyList<Invoice>> LoadInvoicesAsync(
        string? search,
        int page,
        int pageSize,
        bool unlinkedOnly,
        bool unlinkedCreditsOnly,
        CancellationToken ct)
    {
        var query = _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .Include(i => i.Job)
            .Include(i => i.CreditNoteForInvoice)
            .AsQueryable();

        if (unlinkedOnly)
            query = query.Where(i => i.JobId == null);

        if (unlinkedCreditsOnly)
            query = query.Where(i => i.DocumentType == InvoiceDocumentType.CreditNote && i.CreditNoteForInvoiceId == null);

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
        if (invoice.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for an invoice.");

        var customer = await _dbContext.Set<Customer>().FindAsync([invoice.CustomerId], ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found.");

        if (invoice.TaxRate < 0 || invoice.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");

        if (invoice.InvoiceDate != default && invoice.DueDate.Date < invoice.InvoiceDate.Date)
            throw new InvalidOperationException("Due date cannot be before the invoice date.");
        if (invoice.DueDate != default && invoice.DueDate.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Due date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            invoice.Notes = invoice.Notes.Trim();
            if (invoice.Notes.Length > 2000)
                throw new InvalidOperationException("Invoice notes cannot exceed 2000 characters.");
        }

        await ValidateInvoiceJobLinkAsync(invoice.JobId, invoice.CustomerId, ct);

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? invoice.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Invoice, ct);

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            invoice.InvoiceNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Invoice", "INV", ct)
                : $"INV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        else
        {
            invoice.InvoiceNumber = invoice.InvoiceNumber.Trim();
            if (invoice.InvoiceNumber.Length > 50)
                throw new InvalidOperationException("Invoice number cannot exceed 50 characters.");
            var numberTaken = await _dbContext.Set<Invoice>()
                .AnyAsync(i => i.InvoiceNumber == invoice.InvoiceNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Invoice number '{invoice.InvoiceNumber}' already exists.");
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
        var existing = await _dbContext.Set<Invoice>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoice.Id, ct);
        if (existing == null)
            throw new InvalidOperationException("Invoice not found.");
        if (existing.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException(
                $"Cannot edit invoice in status {existing.Status}. Only Draft invoices can be updated.");

        if (invoice.TaxRate < 0 || invoice.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");
        if (invoice.InvoiceDate != default && invoice.DueDate.Date < invoice.InvoiceDate.Date)
            throw new InvalidOperationException("Due date cannot be before the invoice date.");
        if (invoice.DueDate != default && invoice.DueDate.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Due date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            invoice.Notes = invoice.Notes.Trim();
            if (invoice.Notes.Length > 2000)
                throw new InvalidOperationException("Invoice notes cannot exceed 2000 characters.");
        }

        if (invoice.CustomerId == Guid.Empty)
            invoice.CustomerId = existing.CustomerId;
        else if (invoice.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>().FindAsync([invoice.CustomerId], ct);
            if (customer == null || customer.IsDeleted)
                throw new InvalidOperationException("Customer not found.");
        }

        // Job link may be set/changed on draft invoices only (status already gated above).
        await ValidateInvoiceJobLinkAsync(invoice.JobId, invoice.CustomerId, ct);

        // Identity and payment state must not drift via free-form update payloads.
        invoice.InvoiceNumber = existing.InvoiceNumber;
        invoice.Status = existing.Status;
        invoice.AmountPaid = existing.AmountPaid;
        invoice.DocumentType = existing.DocumentType;

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

        if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            && invoice.AmountPaid > 0)
            throw new InvalidOperationException(
                "Cannot delete an invoice with recorded payments. Cancel it instead if supported.");

        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.PartiallyPaid)
            throw new InvalidOperationException("Cannot delete a paid or partially paid invoice.");

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
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == line.InvoiceId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Lines can only be added to draft invoices.");

        ValidateLine(line);

        _dbContext.Set<InvoiceLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        // Reload lines for accurate totals after insert.
        await _dbContext.Entry(invoice).Collection(i => i.Lines).LoadAsync(ct);
        invoice.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(InvoiceLine line, CancellationToken ct = default)
    {
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == line.InvoiceId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Lines can only be edited on draft invoices.");

        ValidateLine(line);

        _dbContext.Set<InvoiceLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        invoice.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<InvoiceLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var invoiceId = line.InvoiceId;
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null) return;

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Lines can only be removed from draft invoices.");

        line.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);

        invoice.RecalculateTotals();
        await _dbContext.SaveChangesAsync(ct);

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
        var tenantIdForJob = _tenantProvider?.GetCurrentTenantId() ?? _dbContext.CurrentTenantId;
        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .Include(j => j.Customer)
            .Include(j => j.Quote)
                .ThenInclude(q => q != null ? q.Lines : null)
            .FirstOrDefaultAsync(j =>
                j.Id == jobId
                && !j.IsDeleted
                && (tenantIdForJob == Guid.Empty || j.TenantId == tenantIdForJob), ct);

        if (job == null)
            throw new InvalidOperationException("Job not found.");

        if (job.Customer == null || job.Customer.IsDeleted)
            throw new InvalidOperationException("Cannot invoice a job whose customer is missing or deleted.");

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
        var invoiceDate = DateTime.UtcNow;
        var invoice = new Invoice
        {
            CustomerId = job.CustomerId,
            JobId = job.Id,
            InvoiceDate = invoiceDate,
            DueDate = CustomerPaymentTerms.DueDateFrom(invoiceDate, job.Customer.PaymentTermsDays),
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
        // Travel extra is a SUM. Including ActualCosts materialises every cost row (PD0085).
        var actualTravel = await _dbContext.Set<JobCost>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.JobId == job.Id && !c.IsDeleted && c.CostType.ToLower() == "travel")
            .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        AddLinesForBillingDocument(invoice, job, documentType, percentOfQuotedTotal, actualTravel);
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

    public Task<Invoice> CreateCreditNoteAsync(Guid sourceInvoiceId, string reason, CancellationToken ct = default) =>
        CreateCreditNoteCoreAsync(sourceInvoiceId, reason, partial: false, inclusiveAmount: null, percentOfTotal: null, ct);

    public Task<Invoice> CreatePartialCreditNoteAsync(
        Guid sourceInvoiceId,
        string reason,
        decimal? inclusiveAmount,
        decimal? percentOfTotal,
        CancellationToken ct = default) =>
        CreateCreditNoteCoreAsync(sourceInvoiceId, reason, partial: true, inclusiveAmount, percentOfTotal, ct);

    private async Task<Invoice> CreateCreditNoteCoreAsync(
        Guid sourceInvoiceId,
        string reason,
        bool partial,
        decimal? inclusiveAmount,
        decimal? percentOfTotal,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A reason is required for a credit note.");
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new InvalidOperationException("Credit note reason must be at least 3 characters.");
        if (reason.Length > 500)
            throw new InvalidOperationException("Credit note reason cannot exceed 500 characters.");

        // IgnoreQueryFilters: soft-deleted customer would otherwise hide the invoice via Include.
        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        var source = await _dbContext.Set<Invoice>()
            .IgnoreQueryFilters()
            .Include(i => i.Lines)
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i =>
                i.Id == sourceInvoiceId
                && !i.IsDeleted
                && (tenantId == Guid.Empty || i.TenantId == tenantId), ct);

        if (source == null)
            throw new InvalidOperationException("Source invoice not found.");

        if (source.DocumentType == InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Cannot create a credit note from another credit note.");

        if (source.DocumentType == InvoiceDocumentType.Proforma)
            throw new InvalidOperationException("Cannot create a credit note from a proforma.");

        if (source.Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot credit a cancelled invoice.");

        if (source.Status == InvoiceStatus.Draft)
            throw new InvalidOperationException("Cannot credit a draft invoice. Send it first or delete it.");

        if (!source.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Source invoice has no lines to credit.");

        // Soft-delete aware customer check so credit notes do not orphan accounting.
        if (source.Customer == null || source.Customer.IsDeleted)
            throw new InvalidOperationException(
                "Cannot create a credit note — customer is missing or deleted.");

        decimal? pinnedSubtotal = null;
        decimal? pinnedTax = null;
        if (partial)
        {
            var inclusive = ResolvePartialCreditAmount(source, inclusiveAmount, percentOfTotal);
            var (subtotal, tax) = InvoiceBillingCalculator.SplitVatInclusive(inclusive, source.TaxRate);
            pinnedSubtotal = subtotal;
            pinnedTax = tax;
        }

        if (tenantId == Guid.Empty)
            tenantId = source.TenantId;
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
            Notes = partial
                ? $"Credit for {source.InvoiceNumber}: {reason} (R {Math.Round(pinnedSubtotal!.Value + pinnedTax!.Value, 2, MidpointRounding.AwayFromZero):N2} incl. VAT)"
                : $"Credit for {source.InvoiceNumber}: {reason}"
        };

        creditNote.InvoiceNumber = _documentSequence != null
            ? await _documentSequence.GetNextNumberAsync("CreditNote", "CN", ct)
            : $"CN-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        _dbContext.Set<Invoice>().Add(creditNote);

        if (partial)
        {
            _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
            {
                InvoiceId = creditNote.Id,
                Description = $"Credit: {reason}",
                Quantity = 1m,
                UnitPrice = pinnedSubtotal!.Value,
                Unit = "each",
                LineType = "Credit"
            });
        }
        else
        {
            foreach (var line in source.Lines.Where(l => !l.IsDeleted))
            {
                _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
                {
                    InvoiceId = creditNote.Id,
                    Description = $"Credit: {line.Description}",
                    Quantity = Math.Abs(line.Quantity),
                    UnitPrice = Math.Abs(line.UnitPrice),
                    Unit = line.Unit,
                    LineType = line.LineType
                });
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        var saved = await GetByIdAsync(creditNote.Id, ct);
        if (saved == null)
            return creditNote;

        saved.RecalculateTotals();
        if (partial)
        {
            // RecalculateTotals rounds net × rate. The office typed a gross amount, so the split wins.
            saved.Subtotal = pinnedSubtotal!.Value;
            saved.Tax = pinnedTax!.Value;
            saved.Total = Math.Round(pinnedSubtotal.Value + pinnedTax.Value, 2, MidpointRounding.AwayFromZero);
        }

        await _dbContext.SaveChangesAsync(ct);
        await SyncDepositReceivedAfterCreditAsync(saved, ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            var detail = partial
                ? $"Partial credit note for {source.InvoiceNumber}: R {saved.Total:N2} incl. VAT. {reason}"
                : $"Credit note for {source.InvoiceNumber}: {reason}";
            await _auditService.LogAsync("CREATE", "Invoice", saved.InvoiceNumber, detail, ct);
        }

        if (_notifications != null)
        {
            var message = partial
                ? $"{source.InvoiceNumber} credited R {saved.Total:N2} incl. VAT (ex-VAT R {saved.Subtotal:N2}, VAT R {saved.Tax:N2}): {reason}. Stored as a positive credit note. It reduces the customer balance once issued."
                : $"{source.InvoiceNumber} credited (R {Math.Abs(saved.Total):N0}): {reason}. Stored as a positive credit note and reduces the customer balance.";
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = saved.TenantId,
                Title = $"Credit note {saved.InvoiceNumber} for {source.InvoiceNumber}",
                Message = message,
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = saved.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }

        return saved;
    }

    /// <summary>
    /// VAT-inclusive amount to credit. Percent is of the source total. The ceiling is the source balance due.
    /// </summary>
    private static decimal ResolvePartialCreditAmount(Invoice source, decimal? inclusiveAmount, decimal? percentOfTotal)
    {
        var hasAmount = inclusiveAmount.HasValue;
        var hasPercent = percentOfTotal.HasValue;
        if (hasAmount && hasPercent)
            throw new InvalidOperationException("Enter either a VAT-inclusive amount or a percent, not both.");
        if (!hasAmount && !hasPercent)
            throw new InvalidOperationException("Enter a VAT-inclusive amount or a percent of the invoice total.");

        decimal amount;
        if (hasPercent)
        {
            var percent = percentOfTotal!.Value;
            if (percent <= 0m || percent > 100m)
                throw new InvalidOperationException("Credit percent must be greater than 0 and at most 100.");

            amount = Math.Round(source.Total * percent / 100m, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            amount = Math.Round(inclusiveAmount!.Value, 2, MidpointRounding.AwayFromZero);
        }

        if (amount <= 0m)
            throw new InvalidOperationException("Credit amount must be greater than zero.");

        var balanceDue = InvoiceBillingCalculator.CalculateBalanceDue(source.Total, source.AmountPaid);
        if (amount > balanceDue)
            throw new InvalidOperationException(
                $"Credit amount R {amount:N2} exceeds the source balance due of R {balanceDue:N2}.");

        return amount;
    }

    public async Task<Invoice> IssueCreditNoteAsync(Guid creditNoteId, CancellationToken ct = default)
    {
        var credit = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == creditNoteId, ct);

        if (credit == null)
            throw new InvalidOperationException("Credit note not found.");

        if (credit.Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot issue a credit note from a cancelled invoice.");

        if (credit.DocumentType == InvoiceDocumentType.Proforma)
            throw new InvalidOperationException("Cannot issue a credit note from a proforma.");

        if (credit.DocumentType != InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Only a draft credit note can be issued.");

        if (credit.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only a draft credit note can be issued.");

        if (!credit.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot issue a credit note with no lines.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (tenantId == Guid.Empty)
            tenantId = credit.TenantId;

        var customerOk = await _dbContext.Set<Customer>()
            .AsNoTracking()
            .AnyAsync(c => c.Id == credit.CustomerId, ct);
        if (!customerOk)
            throw new InvalidOperationException("Cannot issue a credit note — customer is missing or deleted.");

        if (credit.CreditNoteForInvoiceId is Guid parentId)
        {
            var source = await _dbContext.Set<Invoice>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == parentId && !i.IsDeleted, ct);

            if (source == null || source.TenantId != tenantId)
                throw new InvalidOperationException("Cannot issue a credit note — source invoice is missing.");

            if (source.DocumentType == InvoiceDocumentType.CreditNote)
                throw new InvalidOperationException("Cannot issue a credit note from another credit note.");

            if (source.DocumentType == InvoiceDocumentType.Proforma)
                throw new InvalidOperationException("Cannot issue a credit note from a proforma.");

            if (source.Status == InvoiceStatus.Cancelled)
                throw new InvalidOperationException("Cannot issue a credit note from a cancelled invoice.");
        }

        credit.Status = InvoiceStatus.Sent;
        await _dbContext.SaveChangesAsync(ct);
        await SyncDepositReceivedAfterCreditAsync(credit, ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "ISSUE",
                "Invoice",
                credit.InvoiceNumber,
                $"Issued credit note {credit.InvoiceNumber}, VAT-inclusive R {Math.Abs(credit.Total):N2}",
                ct);
        }

        return credit;
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

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenPaymentPopAsync(
        Guid paymentId,
        CancellationToken ct = default)
    {
        if (_documentStorage == null)
            return null;

        var payment = await _dbContext.Set<InvoicePayment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment == null || string.IsNullOrWhiteSpace(payment.PopStorageKey))
            return null;

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? payment.TenantId;
        var stream = await _documentStorage.OpenReadAsync(tenantId, payment.PopStorageKey, ct);
        if (stream == null)
            return null;

        return (
            stream,
            payment.PopFileName ?? "pop.bin",
            payment.PopContentType ?? "application/octet-stream");
    }

    public async Task ReversePaymentAsync(Guid paymentId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A reason is required to reverse a receipt.");
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new InvalidOperationException("Reversal reason must be at least 3 characters.");
        if (reason.Length > 500)
            throw new InvalidOperationException("Reversal reason cannot exceed 500 characters.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? _dbContext.CurrentTenantId;
        var payment = await _dbContext.Set<InvoicePayment>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

        if (payment == null)
            throw new InvalidOperationException("Payment not found.");

        if (payment.TenantId != tenantId)
            throw new InvalidOperationException("Cannot reverse a payment on another tenant.");

        if (payment.IsDeleted)
            throw new InvalidOperationException("This receipt has already been reversed.");

        var invoice = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == payment.InvoiceId, ct);
        if (invoice == null || invoice.TenantId != tenantId)
            throw new InvalidOperationException("Invoice not found.");

        var amount = Math.Round(payment.Amount, 2);
        if (amount <= 0)
            throw new InvalidOperationException("This receipt has no amount to reverse.");

        invoice.AmountPaid = Math.Max(0m, Math.Round(invoice.AmountPaid - amount, 2));
        invoice.Status = InvoiceBillingCalculator.DerivePaymentStatus(
            invoice.Total,
            invoice.AmountPaid,
            invoice.Status,
            invoice.DueDate,
            DateTime.UtcNow);

        var stamp = $"Reversed: {reason}";
        payment.Notes = string.IsNullOrWhiteSpace(payment.Notes)
            ? stamp
            : $"{payment.Notes.Trim()} | {stamp}";
        payment.IsDeleted = true;

        if (invoice.DocumentType == InvoiceDocumentType.Deposit && invoice.JobId is Guid jobId)
        {
            var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job != null && job.DepositReceived && invoice.AmountPaid < invoice.Total)
            {
                var siblings = await _dbContext.Set<Invoice>()
                    .AsNoTracking()
                    .Where(i => i.JobId == jobId && i.Id != invoice.Id)
                    .Select(i => new { i.DocumentType, i.Status })
                    .ToListAsync(ct);
                var otherCountingDeposit = siblings.Any(i =>
                    i.DocumentType == InvoiceDocumentType.Deposit
                    && InvoiceBillingCalculator.CountsTowardJobBilled(i.DocumentType, i.Status));

                if (InvoiceBillingCalculator.ShouldClearDepositReceived(
                        job.DepositReceived,
                        invoice.DocumentType,
                        invoice.Total,
                        invoice.AmountPaid,
                        otherCountingDeposit))
                {
                    job.DepositReceived = false;
                }
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        var reference = payment.Reference;
        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "REVERSE",
                "Invoice",
                invoice.InvoiceNumber,
                $"Reversed receipt R {amount:N2}"
                    + (string.IsNullOrWhiteSpace(reference) ? "" : $" ref {reference.Trim()}")
                    + $". {reason}",
                ct);
        }

        if (_notifications != null)
        {
            var remaining = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = $"Receipt reversed on {invoice.InvoiceNumber}",
                Message = $"R {amount:N2} reversed. Balance due R {remaining:N2}.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }
    }

    public async Task<IReadOnlyList<AllocatableInvoiceRow>> GetAllocatableInvoicesAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            return Array.Empty<AllocatableInvoiceRow>();

        var rows = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId
                && i.DocumentType != InvoiceDocumentType.Proforma
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Draft
                && i.Status != InvoiceStatus.Cancelled)
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.InvoiceNumber)
            .Select(i => new { i.Id, i.InvoiceNumber, i.InvoiceDate, i.Total, i.AmountPaid })
            .ToListAsync(ct);

        return rows
            .Select(i => new AllocatableInvoiceRow(
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid)))
            .Where(r => r.BalanceDue > 0m)
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> AllocateReceiptAsync(
        Guid customerId,
        decimal bankAmount,
        DateTime paymentDate,
        string? reference,
        IReadOnlyList<ReceiptAllocationLine> allocations,
        Guid? recordedByUserId,
        string? notes,
        CancellationToken ct = default)
    {
        if (bankAmount <= 0)
            throw new InvalidOperationException("Payment amount must be positive.");
        if (bankAmount > 100_000_000m)
            throw new InvalidOperationException("Payment amount cannot exceed 100,000,000.");

        paymentDate = paymentDate == default ? DateTime.UtcNow.Date : paymentDate.Date;
        if (paymentDate > DateTime.UtcNow.Date.AddDays(1))
            throw new InvalidOperationException("Payment date cannot be more than one day in the future.");
        if (paymentDate < DateTime.UtcNow.Date.AddYears(-2))
            throw new InvalidOperationException("Payment date cannot be more than 2 years in the past.");

        if (string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("A receipt reference is required when allocating across invoices.");
        reference = reference.Trim();
        if (reference.Length > 100)
            throw new InvalidOperationException("Payment reference cannot exceed 100 characters.");

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 500)
            throw new InvalidOperationException("Payment notes cannot exceed 500 characters.");
        var paymentNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        var lines = new List<(Guid InvoiceId, decimal Amount)>();
        if (allocations != null)
        {
            foreach (var line in allocations)
            {
                var amount = ToCents(line.Amount);
                if (amount <= 0)
                    throw new InvalidOperationException("Each allocation must be a positive amount.");
                lines.Add((line.InvoiceId, amount));
            }
        }

        if (lines.Count < 2)
            throw new InvalidOperationException("Allocate a receipt across at least two invoices.");

        if (lines.Select(l => l.InvoiceId).Distinct().Count() != lines.Count)
            throw new InvalidOperationException("Each invoice can appear only once on a receipt.");

        var bank = ToCents(bankAmount);
        var sum = ToCents(lines.Sum(l => l.Amount));
        if (sum != bank)
            throw new InvalidOperationException(
                $"Allocations R {sum:N2} do not add up to the bank amount R {bank:N2}.");

        if (customerId == Guid.Empty)
            throw new InvalidOperationException("Customer not found.");

        var customer = await _dbContext.Set<Customer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer == null)
            throw new InvalidOperationException("Customer not found.");

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? _dbContext.CurrentTenantId;
        var ids = lines.Select(l => l.InvoiceId).ToList();
        var invoices = await _dbContext.Set<Invoice>()
            .IgnoreQueryFilters()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct);
        var byId = invoices.ToDictionary(i => i.Id);

        var ordered = new List<(Invoice Invoice, decimal Amount)>(lines.Count);
        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.InvoiceId, out var invoice) || invoice.IsDeleted)
                throw new InvalidOperationException("Invoice not found.");

            if (invoice.TenantId != tenantId)
                throw new InvalidOperationException("Cannot allocate a receipt to an invoice on another tenant.");

            if (invoice.CustomerId != customerId)
                throw new InvalidOperationException("All invoices on one receipt must belong to the same customer.");

            if (invoice.DocumentType == InvoiceDocumentType.CreditNote)
                throw new InvalidOperationException(
                    $"Payments cannot be recorded against credit note {invoice.InvoiceNumber}.");

            if (invoice.DocumentType == InvoiceDocumentType.Proforma)
                throw new InvalidOperationException(
                    $"Payments cannot be recorded against proforma {invoice.InvoiceNumber}.");

            if (invoice.Status == InvoiceStatus.Cancelled)
                throw new InvalidOperationException(
                    $"Cannot allocate a receipt to cancelled invoice {invoice.InvoiceNumber}.");

            if (invoice.Status == InvoiceStatus.Draft)
                throw new InvalidOperationException(
                    $"Send {invoice.InvoiceNumber} before allocating a receipt.");

            var balance = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
            if (balance <= 0m)
                throw new InvalidOperationException($"Invoice {invoice.InvoiceNumber} is already fully paid.");

            if (line.Amount > balance)
                throw new InvalidOperationException(
                    $"Payment R {line.Amount:N2} exceeds balance due R {balance:N2} on {invoice.InvoiceNumber}.");

            ordered.Add((invoice, line.Amount));
        }

        var paymentIds = new List<Guid>(ordered.Count);
        foreach (var (invoice, amount) in ordered)
        {
            var payment = new InvoicePayment
            {
                InvoiceId = invoice.Id,
                Amount = amount,
                PaymentDate = paymentDate,
                Reference = reference,
                RecordedByUserId = recordedByUserId,
                Notes = paymentNotes
            };
            _dbContext.Set<InvoicePayment>().Add(payment);
            paymentIds.Add(payment.Id);

            invoice.AmountPaid = Math.Round(invoice.AmountPaid + amount, 2);
            invoice.Status = InvoiceBillingCalculator.DerivePaymentStatus(
                invoice.Total,
                invoice.AmountPaid,
                invoice.Status,
                invoice.DueDate,
                DateTime.UtcNow);
        }

        await MarkDepositsReceivedAsync(ordered.Select(o => o.Invoice), ct);

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        foreach (var (invoice, amount) in ordered)
        {
            var remaining = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
            await TryEmailPaymentReceiptAsync(invoice, amount, remaining, ct);
        }

        var invoiceNumbers = ordered.Select(o => o.Invoice.InvoiceNumber).ToList();
        var listed = string.Join(", ", ordered.Select(o => $"{o.Invoice.InvoiceNumber} R {o.Amount:N2}"));
        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "ALLOCATE",
                "Invoice",
                reference,
                $"Allocated receipt R {bank:N2} ref {reference} to {listed}",
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = tenantId,
                Title = $"Receipt allocated ref {reference}",
                Message = $"R {bank:N2} allocated to {string.Join(", ", invoiceNumbers)}.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = ordered[0].Invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }

        return paymentIds;
    }

    /// <summary>
    /// A draft credit does not reduce cover, so creating one leaves the flag alone.
    /// Issuing it clears the flag when the job is still open and the deposit is no longer covered.
    /// </summary>
    private async Task SyncDepositReceivedAfterCreditAsync(Invoice credit, CancellationToken ct)
    {
        if (credit.JobId is not Guid jobId)
            return;

        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || !job.DepositReceived)
            return;

        if (job.Status is JobStatus.Closed or JobStatus.Cancelled)
            return;

        var rows = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.JobId == jobId)
            .Select(i => new { i.Id, i.DocumentType, i.Status, i.Total, i.CreditNoteForInvoiceId })
            .ToListAsync(ct);

        var cover = InvoiceBillingCalculator.SummarizeDepositCover(rows.Select(i =>
            new InvoiceBillingCalculator.BillingDocumentSlice(
                i.Id, i.DocumentType, i.Status, i.Total, i.CreditNoteForInvoiceId)));

        if (!InvoiceBillingCalculator.ShouldClearDepositReceivedAfterCredit(
                job.DepositReceived,
                job.Status,
                job.QuotedTotal,
                job.DepositPercent,
                cover.BilledCover,
                cover.HasCountingDeposit))
            return;

        job.DepositReceived = false;
        await _dbContext.SaveChangesAsync(ct);
        if (_cache != null)
            await TenantCacheInvalidation.OnJobMutatedAsync(_cache, ct);
    }

    private async Task MarkDepositsReceivedAsync(IEnumerable<Invoice> invoices, CancellationToken ct)
    {
        var jobIds = invoices
            .Where(i => i.DocumentType == InvoiceDocumentType.Deposit
                && i.JobId.HasValue
                && i.AmountPaid >= i.Total)
            .Select(i => i.JobId!.Value)
            .Distinct()
            .ToList();
        if (jobIds.Count == 0)
            return;

        var jobs = await _dbContext.Set<Job>()
            .Where(j => jobIds.Contains(j.Id) && !j.DepositReceived)
            .ToListAsync(ct);
        foreach (var job in jobs)
            job.DepositReceived = true;
    }

    private static decimal ToCents(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public async Task<IReadOnlyList<AgedDebtorRow>> GetAgedDebtorsAsync(CancellationToken ct = default)
    {
        // Headline stays bounded for the MET seed (~16k invoices). Netting does not:
        // a linked parent or the customer's real oldest invoice can sit outside the
        // oldest-N window, and applying credits only to that window hits the wrong debt.
        const int candidateWindow = 200;
        const int headlineTake = 50;
        var now = DateTime.UtcNow;

        var creditRows = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.DocumentType == InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Draft)
            .Select(i => new { i.CustomerId, i.CreditNoteForInvoiceId, i.Total, i.AmountPaid })
            .ToListAsync(ct);
        var credits = creditRows
            .Select(i => new InvoiceCreditConvention.OpenCredit(
                i.CustomerId,
                i.CreditNoteForInvoiceId,
                InvoiceCreditConvention.OpenCreditMagnitude(i.Total, i.AmountPaid)))
            .Where(i => i.Magnitude > 0m)
            .ToList();

        var openSales = _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.DocumentType != InvoiceDocumentType.Proforma
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Draft);

        var headlineCandidates = await openSales
            .Where(i => i.DueDate < now)
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.Id)
            .Take(candidateWindow)
            .ToListAsync(ct);

        var invoicesById = new Dictionary<Guid, Invoice>(headlineCandidates.Count);
        foreach (var invoice in headlineCandidates)
            invoicesById[invoice.Id] = invoice;

        var creditCustomerIds = credits.Select(c => c.CustomerId).Distinct().ToList();
        if (creditCustomerIds.Count > 0)
        {
            // Every open invoice for a customer with an open credit, including ones that
            // are not yet overdue, so a linked parent absorbs the credit before any leftover
            // falls through to that customer's oldest overdue balance.
            var creditCustomerInvoices = await openSales
                .Where(i => creditCustomerIds.Contains(i.CustomerId))
                .ToListAsync(ct);
            foreach (var invoice in creditCustomerInvoices)
                invoicesById.TryAdd(invoice.Id, invoice);
        }

        var invoices = invoicesById.Values.ToList();
        var net = InvoiceCreditConvention.NetAgedBalances(
            invoices.Select(i => new InvoiceCreditConvention.AgedInvoiceSlice(
                i.Id,
                i.CustomerId,
                i.DueDate,
                InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid))).ToList(),
            credits);

        return invoices
            .Where(i => i.DueDate < now)
            .Select(i =>
            {
                var balance = net.TryGetValue(i.Id, out var netBalance)
                    ? netBalance
                    : InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid);
                var days = InvoiceBillingCalculator.GetDaysOverdue(i.DueDate, now);
                return new AgedDebtorRow(
                    i.Id,
                    i.InvoiceNumber,
                    i.Customer?.Name ?? "-",
                    i.DueDate,
                    i.Total,
                    i.AmountPaid,
                    balance,
                    days,
                    InvoiceBillingCalculator.GetAgingBucket(days));
            })
            .Where(r => r.BalanceDue > 0)
            .OrderByDescending(r => r.DaysOverdue)
            .ThenBy(r => r.InvoiceId)
            .Take(headlineTake)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerInCreditRow>> GetCustomersInCreditAsync(CancellationToken ct = default)
    {
        // Header columns only. ArSignedOpenBalance drops draft, proforma, cancelled,
        // and settled documents, so a draft credit cannot put a customer in credit.
        var documents = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Select(i => new { i.CustomerId, i.DocumentType, i.Status, i.Total, i.AmountPaid })
            .ToListAsync(ct);

        var inCredit = documents
            .GroupBy(i => i.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Signed = Math.Round(
                    g.Sum(i => InvoiceCreditConvention.ArSignedOpenBalance(
                        i.DocumentType, i.Status, i.Total, i.AmountPaid)),
                    2,
                    MidpointRounding.AwayFromZero)
            })
            .Where(x => x.Signed < 0m)
            .ToList();

        if (inCredit.Count == 0)
            return Array.Empty<CustomerInCreditRow>();

        var ids = inCredit.Select(x => x.CustomerId).ToList();
        var names = await _dbContext.Set<Customer>()
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var nameById = names.ToDictionary(c => c.Id, c => c.Name);

        return inCredit
            .Select(x => new CustomerInCreditRow(
                x.CustomerId,
                nameById.TryGetValue(x.CustomerId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "-",
                -x.Signed))
            .OrderByDescending(r => r.CreditAmount)
            .ThenBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CustomerId)
            .ToList();
    }

    public async Task<CustomerStatement?> GetCustomerStatementAsync(
        Guid customerId,
        DateTime? asOfUtc = null,
        CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            return null;

        var customer = await _dbContext.Set<Customer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer == null)
            return null;

        var rows = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.CustomerId == customerId)
            .Select(i => new
            {
                i.InvoiceDate,
                i.DocumentType,
                i.Status,
                i.InvoiceNumber,
                i.Total,
                i.AmountPaid,
                i.IsDeleted,
                Payments = i.Payments.Select(p => new
                {
                    p.PaymentDate,
                    p.Amount,
                    p.Reference,
                    p.IsDeleted
                }).ToList()
            })
            .ToListAsync(ct);

        var documents = rows.Select(i => new CustomerStatementDocument(
            i.InvoiceDate,
            i.DocumentType,
            i.Status,
            i.InvoiceNumber,
            i.Total,
            i.AmountPaid,
            i.IsDeleted,
            i.Payments
                .Select(p => new CustomerStatementReceipt(p.PaymentDate, p.Amount, p.Reference, p.IsDeleted))
                .ToList())).ToList();

        return CustomerStatementBuilder.Build(customer.Id, customer.Name, asOfUtc ?? DateTime.UtcNow, documents);
    }

    public async Task<IReadOnlyList<ConvertibleDocumentRow>> GetUnsentQueueAsync(
        int take = 20,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 50);
        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i => i.Status == InvoiceStatus.Draft && i.Lines.Any())
            .OrderByDescending(i => i.Total)
            .ThenBy(i => i.InvoiceNumber)
            .Take(take)
            .ToListAsync(ct);

        return invoices
            .Select(i => new ConvertibleDocumentRow(
                i.Id,
                "Invoice",
                i.InvoiceNumber,
                i.Customer?.Name ?? "—",
                i.Total,
                $"/invoices?open={i.Id:D}"))
            .ToList();
    }

    public async Task<InvoiceChaseResult> ChaseOverdueAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Customer)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException("Invoice not found.");

        if (invoice.DocumentType is InvoiceDocumentType.Proforma or InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Cannot chase a proforma or credit note.");
        if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot chase a draft or cancelled invoice.");

        var gross = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
        if (invoice.Status == InvoiceStatus.Paid || gross <= 0.01m)
            throw new InvalidOperationException("Invoice is already fully paid.");

        var days = InvoiceBillingCalculator.GetDaysOverdue(invoice.DueDate, DateTime.UtcNow);
        if (days <= 0)
            throw new InvalidOperationException("Invoice is not overdue yet.");

        // Receipts are already in AmountPaid. Open credits net the same way as aged debtors,
        // so the reminder asks for what is still owed, not the original gross.
        var balance = await NetChaseBalanceAsync(invoice, gross, ct);
        if (balance <= 0.01m)
            throw new InvalidOperationException("Invoice has nothing left to chase after credits.");

        var customer = invoice.Customer
            ?? await _dbContext.Set<Customer>()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == invoice.CustomerId, ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Cannot chase — customer is missing or deleted.");

        var email = customer.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException(
                "Cannot chase — customer has no email. Add an email on the customer record first.");

        var todayStamp = $"Chased {DateTime.UtcNow:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(invoice.Notes)
            && invoice.Notes.Contains(todayStamp, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.InvoiceNumber} was already chased today.");
        }

        var emailSent = false;
        if (_email?.IsConfigured == true)
        {
            var html = $"""
                <p>This is a payment reminder from your contractor.</p>
                <ul>
                  <li><strong>Invoice:</strong> {invoice.InvoiceNumber}</li>
                  <li><strong>Due:</strong> {invoice.DueDate:yyyy-MM-dd} ({days} day(s) overdue)</li>
                  <li><strong>Balance due:</strong> R {balance:N2}</li>
                </ul>
                <p>Please arrange payment at your earliest convenience.</p>
                """;
            await _email.SendEmailAsync(email, $"Payment reminder — invoice {invoice.InvoiceNumber}", html, ct);
            emailSent = true;
        }

        invoice.Notes = string.IsNullOrWhiteSpace(invoice.Notes)
            ? todayStamp
            : $"{invoice.Notes.Trim()}\n{todayStamp}";
        if (invoice.Notes.Length > 2000)
            invoice.Notes = invoice.Notes[^2000..];

        if (invoice.Status == InvoiceStatus.Sent)
            invoice.Status = InvoiceStatus.Overdue;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_auditService != null)
        {
            var channel = emailSent ? $"emailed {email}" : "logged (SMTP not configured)";
            await _auditService.LogAsync(
                "CHASE",
                "Invoice",
                invoice.InvoiceNumber,
                $"Overdue chase — R {balance:N2}, {days} day(s), {channel}",
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = $"Chased {invoice.InvoiceNumber}",
                Message = $"{customer.Name}: R {balance:N2} outstanding, {days} day(s) overdue."
                    + (emailSent ? $" Reminder emailed to {email}." : " SMTP not configured — chase recorded only."),
                Category = "collections",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }

        return new InvoiceChaseResult(invoice.Id, invoice.InvoiceNumber, emailSent, email, days, balance);
    }

    private async Task<decimal> NetChaseBalanceAsync(Invoice invoice, decimal gross, CancellationToken ct)
    {
        var openSales = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.CustomerId == invoice.CustomerId
                && i.DocumentType != InvoiceDocumentType.Proforma
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Draft)
            .Select(i => new { i.Id, i.CustomerId, i.DueDate, i.Total, i.AmountPaid })
            .ToListAsync(ct);

        var slices = openSales
            .Select(i => new InvoiceCreditConvention.AgedInvoiceSlice(
                i.Id,
                i.CustomerId,
                i.DueDate,
                InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid)))
            .ToList();
        if (slices.TrueForAll(s => s.InvoiceId != invoice.Id))
        {
            slices.Add(new InvoiceCreditConvention.AgedInvoiceSlice(
                invoice.Id,
                invoice.CustomerId,
                invoice.DueDate,
                gross));
        }

        var creditRows = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.DocumentType == InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid
                && i.Status != InvoiceStatus.Draft
                && (i.CustomerId == invoice.CustomerId || i.CreditNoteForInvoiceId == invoice.Id))
            .Select(i => new { i.CustomerId, i.CreditNoteForInvoiceId, i.Total, i.AmountPaid })
            .ToListAsync(ct);

        var credits = creditRows
            .Select(i => new InvoiceCreditConvention.OpenCredit(
                i.CustomerId,
                i.CreditNoteForInvoiceId,
                InvoiceCreditConvention.OpenCreditMagnitude(i.Total, i.AmountPaid)))
            .Where(i => i.Magnitude > 0m)
            .ToList();
        if (credits.Count == 0)
            return gross;

        var net = InvoiceCreditConvention.NetAgedBalances(slices, credits);
        return net.TryGetValue(invoice.Id, out var balance) ? balance : gross;
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
        decimal? percentOfQuotedTotal,
        decimal actualTravel)
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
                    AddQuoteOrSummaryLines(invoice, job, actualTravel);
                }

                break;
            }
            default:
                AddQuoteOrSummaryLines(invoice, job, actualTravel);
                break;
        }
    }

    private void AddQuoteOrSummaryLines(Invoice invoice, Job job, decimal actualTravel)
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
            AddAdditionalTravelLine(invoice, job, actualTravel);
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

    /// <summary>
    /// Bill actual job travel that exceeds quoted travel so extra site travel is not left uninvoiced.
    /// </summary>
    private void AddAdditionalTravelLine(Invoice invoice, Job job, decimal actualTravel)
    {
        var quotedTravel = job.Quote?.Lines?
            .Where(l => !l.IsDeleted && l.LineType.Equals("Travel", StringComparison.OrdinalIgnoreCase))
            .Sum(l => l.LineTotal) ?? 0m;
        var extra = Math.Round(actualTravel - quotedTravel, 2);
        if (extra <= 0.01m)
            return;

        _dbContext.Set<InvoiceLine>().Add(new InvoiceLine
        {
            InvoiceId = invoice.Id,
            Description = $"Additional travel vs quote — Job {job.JobNumber}",
            Quantity = 1,
            UnitPrice = extra,
            LineType = "Travel"
        });
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
        if (amount > 100_000_000m)
            throw new InvalidOperationException("Payment amount cannot exceed 100,000,000.");

        paymentDate = paymentDate == default ? DateTime.UtcNow.Date : paymentDate.Date;
        if (paymentDate > DateTime.UtcNow.Date.AddDays(1))
            throw new InvalidOperationException("Payment date cannot be more than one day in the future.");
        if (paymentDate < DateTime.UtcNow.Date.AddYears(-2))
            throw new InvalidOperationException("Payment date cannot be more than 2 years in the past.");

        var invoice = await _dbContext.Set<Invoice>()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice == null)
            throw new InvalidOperationException("Invoice not found.");

        if (invoice.DocumentType == InvoiceDocumentType.Proforma)
            throw new InvalidOperationException("Payments cannot be recorded against proforma invoices.");

        if (invoice.DocumentType == InvoiceDocumentType.CreditNote)
            throw new InvalidOperationException("Payments cannot be recorded against credit notes.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cannot record payment on a cancelled invoice.");

        if (invoice.Status == InvoiceStatus.Draft)
            throw new InvalidOperationException("Send the invoice before recording payments.");

        var balance = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
        if (balance <= 0.01m)
            throw new InvalidOperationException("Invoice is already fully paid.");

        if (amount > balance + 0.01m)
            throw new InvalidOperationException(
                $"Payment R {amount:N2} exceeds balance due R {balance:N2}.");

        if (!string.IsNullOrWhiteSpace(reference) && reference.Trim().Length > 100)
            throw new InvalidOperationException("Payment reference cannot exceed 100 characters.");
        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 500)
            throw new InvalidOperationException("Payment notes cannot exceed 500 characters.");

        var payment = new InvoicePayment
        {
            InvoiceId = invoiceId,
            Amount = amount,
            PaymentDate = paymentDate,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            RecordedByUserId = recordedByUserId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
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

        // Deposit invoices: mark job deposit received once fully paid.
        var depositJustReceived = false;
        Job? depositJob = null;
        if (invoice.DocumentType == InvoiceDocumentType.Deposit
            && invoice.JobId.HasValue
            && invoice.AmountPaid >= invoice.Total)
        {
            depositJob = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == invoice.JobId.Value, ct);
            if (depositJob != null && !depositJob.DepositReceived)
            {
                depositJob.DepositReceived = true;
                depositJustReceived = true;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        var remaining = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
        var receiptNote = await TryEmailPaymentReceiptAsync(invoice, amount, remaining, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "PAYMENT",
                "Invoice",
                invoice.InvoiceNumber,
                $"Recorded payment R {amount:N2}" + (reference != null ? $" ref {reference}" : "")
                + (popFileName != null ? $" POP {popFileName}" : "")
                + receiptNote,
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = depositJustReceived
                    ? $"Deposit received on {invoice.InvoiceNumber}"
                    : $"Payment received on {invoice.InvoiceNumber}",
                Message = depositJustReceived
                    ? $"R {amount:N2} cleared the deposit on {depositJob?.JobNumber ?? "the job"}. Mobilisation can proceed.{receiptNote}"
                    : $"R {amount:N2} recorded. Balance due R {remaining:N2}.{receiptNote}",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }

        return payment.Id;
    }

    /// <summary>
    /// Best-effort receipt. Payment is already committed — never throw from here.
    /// </summary>
    private async Task<string> TryEmailPaymentReceiptAsync(
        Invoice invoice,
        decimal amount,
        decimal remaining,
        CancellationToken ct)
    {
        try
        {
            var customer = invoice.Customer
                ?? await _dbContext.Set<Customer>()
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.Id == invoice.CustomerId
                        && (invoice.TenantId == Guid.Empty || c.TenantId == invoice.TenantId), ct);
            var email = customer?.Email?.Trim();
            if (customer == null || customer.IsDeleted || string.IsNullOrWhiteSpace(email))
                return " Customer has no email — receipt not sent.";
            if (_email?.IsConfigured != true)
                return " SMTP not configured — receipt recorded in-system only.";

            var html = $"""
                <p>We have received your payment.</p>
                <ul>
                  <li><strong>Invoice:</strong> {invoice.InvoiceNumber}</li>
                  <li><strong>Amount received:</strong> R {amount:N2}</li>
                  <li><strong>Balance due:</strong> R {remaining:N2}</li>
                </ul>
                <p>Thank you.</p>
                """;
            await _email.SendEmailAsync(email, $"Payment received — invoice {invoice.InvoiceNumber}", html, ct);
            return $" Receipt emailed to {email}.";
        }
        catch
        {
            return " Receipt email failed.";
        }
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.Invoices);

    /// <summary>
    /// Ensures optional JobId points at a real job for the same customer and is not cancelled.
    /// Closed jobs may still receive credit notes / adjustments via free-form draft invoices.
    /// </summary>
    private async Task ValidateInvoiceJobLinkAsync(Guid? jobId, Guid customerId, CancellationToken ct)
    {
        if (jobId is null || jobId == Guid.Empty)
            return;

        var job = await _dbContext.Set<Job>().AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId.Value, ct);
        if (job == null || job.IsDeleted)
            throw new InvalidOperationException("Linked job not found or deleted.");

        if (job.CustomerId != customerId)
            throw new InvalidOperationException(
                "Invoice customer must match the linked job's customer.");

        if (job.Status == JobStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot link an invoice to cancelled job {job.JobNumber}.");
    }

    private static void ValidateLine(InvoiceLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Description))
            throw new InvalidOperationException("Line description is required.");
        if (line.Quantity == 0)
            throw new InvalidOperationException("Line quantity cannot be zero.");
        if (Math.Abs(line.Quantity) > 1_000_000m)
            throw new InvalidOperationException("Line quantity magnitude cannot exceed 1,000,000.");
        // Negative quantity is allowed for credit-style adjustments on draft invoices.
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
        var invoice = await _dbContext.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null) return;

        if (invoice.Status == newStatus)
            return;

        // Guard illegal transitions that would lose payment integrity.
        if (invoice.Status == InvoiceStatus.Paid && newStatus is not InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Paid invoices cannot change status except to Cancelled when voiding.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("Cancelled invoices cannot change status.");

        if (newStatus == InvoiceStatus.Paid && invoice.AmountPaid + 0.01m < invoice.Total)
            throw new InvalidOperationException(
                $"Cannot mark paid — balance remaining R {InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid):N2}.");

        if (newStatus == InvoiceStatus.Sent && invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Only draft invoices can be marked Sent.");

        if (newStatus == InvoiceStatus.Sent
            && !invoice.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot send an invoice with no lines.");

        // A credit note is issued onto the account. It must not wait on a customer email,
        // and it must re-check the source document before it reduces the statement.
        if (newStatus == InvoiceStatus.Sent && invoice.DocumentType == InvoiceDocumentType.CreditNote)
        {
            await IssueCreditNoteAsync(invoiceId, ct);
            return;
        }

        Customer? sentTo = null;
        if (newStatus == InvoiceStatus.Sent)
        {
            sentTo = await _dbContext.Set<Customer>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == invoice.CustomerId
                    && (invoice.TenantId == Guid.Empty || c.TenantId == invoice.TenantId), ct);
            if (sentTo == null || sentTo.IsDeleted)
                throw new InvalidOperationException(
                    "Cannot send invoice — customer is missing or deleted.");
            if (string.IsNullOrWhiteSpace(sentTo.Email))
                throw new InvalidOperationException(
                    "Cannot send invoice — customer has no email. Add an email so you can chase payment.");
        }

        if (newStatus == InvoiceStatus.Cancelled && invoice.AmountPaid > 0)
            throw new InvalidOperationException(
                "Cannot cancel an invoice with payments. Create a credit note instead.");

        invoice.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        var emailNote = "";
        if (newStatus == InvoiceStatus.Sent && sentTo != null)
            emailNote = await TryEmailInvoiceSentAsync(invoice, sentTo, ct);

        if (_auditService != null)
        {
            await _auditService.LogAsync(
                "STATUS",
                "Invoice",
                invoice.InvoiceNumber,
                $"Status → {newStatus}{emailNote}",
                ct);
        }

        if (newStatus == InvoiceStatus.Cancelled && _notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = $"Invoice {invoice.InvoiceNumber} was cancelled",
                Message = $"{invoice.InvoiceNumber} (R {invoice.Total:N0}) is no longer collectable. Raise a replacement if the work is still billable.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
        }
    }

    private async Task<string> TryEmailInvoiceSentAsync(Invoice invoice, Customer customer, CancellationToken ct)
    {
        var email = customer.Email!.Trim();
        if (_email?.IsConfigured != true)
            return " (SMTP not configured — recorded sent in-system only)";

        var html = $"""
            <p>Please find invoice <strong>{invoice.InvoiceNumber}</strong>.</p>
            <ul>
              <li><strong>Total:</strong> R {invoice.Total:N2}</li>
              <li><strong>Due:</strong> {invoice.DueDate:yyyy-MM-dd}</li>
            </ul>
            <p>Contact us if you have any questions.</p>
            """;
        await _email.SendEmailAsync(email, $"Invoice {invoice.InvoiceNumber}", html, ct);
        return $" (emailed {email})";
    }
}
