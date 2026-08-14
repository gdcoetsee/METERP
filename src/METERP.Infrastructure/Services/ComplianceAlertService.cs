using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class ComplianceAlertService : IComplianceAlertService
{
    private const string ComplianceRoles = "Executive,HrManager";

    private readonly AppDbContext _dbContext;
    private readonly ITenantNotificationService _notifications;
    private readonly IAuditService? _audit;

    public ComplianceAlertService(
        AppDbContext dbContext,
        ITenantNotificationService notifications,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<int> RunExpiryScanAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var alertsCreated = 0;

        alertsCreated += await ScanCompanyDocumentsAsync(now, ct);
        alertsCreated += await ScanEmployeeCertificationsAsync(now, ct);

        if (alertsCreated > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "COMPLIANCE_SCAN",
                "Compliance",
                "expiry-alerts",
                $"Created {alertsCreated} expiry notification(s)",
                ct);
        }

        return alertsCreated;
    }

    public async Task<int> RunOverdueInvoiceScanAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow.Date;
        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Include(i => i.Customer)
            .Where(i =>
                i.DueDate < now
                && i.DocumentType != InvoiceDocumentType.Proforma
                && i.DocumentType != InvoiceDocumentType.CreditNote
                && i.Status != InvoiceStatus.Draft
                && i.Status != InvoiceStatus.Cancelled
                && i.Status != InvoiceStatus.Paid)
            .ToListAsync(ct);

        var overdue = invoices
            .Where(i => InvoiceBillingCalculator.CalculateBalanceDue(i.Total, i.AmountPaid) > 0)
            .ToList();
        if (overdue.Count == 0)
            return 0;

        var ids = overdue.Select(i => i.Id).ToList();
        var alreadyAlerted = await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "collections"
                && n.RelatedEntityType == nameof(Invoice)
                && n.RelatedEntityId != null
                && ids.Contains(n.RelatedEntityId.Value))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct);
        var alerted = alreadyAlerted.ToHashSet();

        var created = 0;
        foreach (var invoice in overdue)
        {
            if (alerted.Contains(invoice.Id))
                continue;

            var days = InvoiceBillingCalculator.GetDaysOverdue(invoice.DueDate, now);
            var balance = InvoiceBillingCalculator.CalculateBalanceDue(invoice.Total, invoice.AmountPaid);
            var customer = invoice.Customer?.Name ?? "Customer";

            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = invoice.TenantId,
                Title = $"Invoice {invoice.InvoiceNumber} is {days} day(s) overdue",
                Message = $"{customer}: R {balance:N2} outstanding, due {invoice.DueDate:yyyy-MM-dd}. Chase payment to keep cash moving.",
                Category = "collections",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            }, ct);
            created++;
        }

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "COLLECTIONS_SCAN",
                "Invoice",
                "overdue-alerts",
                $"Created {created} overdue invoice notification(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunApprovalSlaScanAsync(CancellationToken ct = default)
    {
        var slaHours = await GetSlaHoursAsync(ct);
        var cutoff = DateTime.UtcNow.AddHours(-slaHours);
        var created = 0;

        var quotes = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .Where(q =>
                q.ApprovalStatus == QuoteApprovalStatus.PendingExecutive
                && q.SubmittedForApprovalAt != null
                && q.SubmittedForApprovalAt <= cutoff)
            .ToListAsync(ct);

        created += await NotifySlaItemsAsync(
            quotes.Select(q => (q.Id, q.TenantId, Title: $"Quote {q.QuoteNumber} is past approval SLA",
                Message: $"{q.Customer?.Name ?? "Customer"} — submitted {q.SubmittedForApprovalAt:yyyy-MM-dd HH:mm} UTC, SLA {slaHours}h.")),
            nameof(Quote),
            ct);

        var requisitions = await _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Job)
            .Where(r =>
                r.Status == RequisitionStatus.PendingManager
                || r.Status == RequisitionStatus.PendingExecutive)
            .ToListAsync(ct);

        var overdueReqs = requisitions.Where(r =>
        {
            var submitted = r.ManagerApprovedAt ?? r.CreatedDate;
            return submitted <= cutoff;
        }).ToList();

        created += await NotifySlaItemsAsync(
            overdueReqs.Select(r => (r.Id, r.TenantId, Title: $"Requisition {r.RequisitionNumber} is past approval SLA",
                Message: $"{r.Job?.JobNumber ?? "Job"} — waiting {r.Status}, SLA {slaHours}h.")),
            nameof(StockRequisition),
            ct);

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "SLA_SCAN",
                "Approval",
                "sla-alerts",
                $"Created {created} approval SLA notification(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunExpiredQuoteScanAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var quotes = await _dbContext.Set<Quote>()
            .Include(q => q.Customer)
            .Where(q =>
                (q.Status == QuoteStatus.Sent || q.Status == QuoteStatus.Accepted)
                && q.ValidUntil < today)
            .ToListAsync(ct);

        if (quotes.Count == 0)
            return 0;

        var ids = quotes.Select(q => q.Id).ToList();
        var converted = (await _dbContext.Set<Job>().AsNoTracking()
            .Where(j => j.QuoteId != null && ids.Contains(j.QuoteId.Value))
            .Select(j => j.QuoteId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "sales"
                && n.RelatedEntityType == nameof(Quote)
                && n.RelatedEntityId != null
                && ids.Contains(n.RelatedEntityId.Value))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var quote in quotes)
        {
            if (converted.Contains(quote.Id))
                continue;

            quote.Status = QuoteStatus.Expired;

            if (!already.Contains(quote.Id))
            {
                await _notifications.CreateAsync(new TenantNotification
                {
                    TenantId = quote.TenantId,
                    Title = $"Quote {quote.QuoteNumber} expired without a job",
                    Message = $"{quote.Customer?.Name ?? "Customer"} — valid until {quote.ValidUntil:yyyy-MM-dd}. Follow up or write it off.",
                    Category = "sales",
                    TargetRoles = "Admin,Executive",
                    RelatedEntityId = quote.Id,
                    RelatedEntityType = nameof(Quote)
                }, ct);
                created++;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "QUOTE_EXPIRY_SCAN",
                "Quote",
                "expired-quotes",
                $"Expired {created} unconverted quote(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunOverduePurchaseOrderScanAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var orders = await _dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p =>
                (p.Status == PurchaseOrderStatus.Sent || p.Status == PurchaseOrderStatus.PartiallyReceived)
                && p.ExpectedDate != null
                && p.ExpectedDate < today)
            .ToListAsync(ct);

        if (orders.Count == 0)
            return 0;

        var ids = orders.Select(p => p.Id).ToList();
        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "procurement"
                && n.RelatedEntityType == nameof(PurchaseOrder)
                && n.RelatedEntityId != null
                && ids.Contains(n.RelatedEntityId.Value)
                && n.Title.Contains("overdue"))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var po in orders)
        {
            if (already.Contains(po.Id))
                continue;

            var days = (today - po.ExpectedDate!.Value.Date).Days;
            var supplier = po.Supplier?.Name ?? "Supplier";
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = po.TenantId,
                Title = $"PO {po.PoNumber} is overdue",
                Message = $"{supplier}: expected {po.ExpectedDate:yyyy-MM-dd} ({days} day(s) late). Chase delivery so the job is not blocked.",
                Category = "procurement",
                TargetRoles = "Admin,Executive,Procurement",
                RelatedEntityId = po.Id,
                RelatedEntityType = nameof(PurchaseOrder)
            }, ct);
            created++;
        }

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "PO_OVERDUE_SCAN",
                "PurchaseOrder",
                "overdue-pos",
                $"Created {created} overdue purchase order notification(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunStuckReadyToInvoiceScanAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-2);
        var jobs = await _dbContext.Set<Job>()
            .AsNoTracking()
            .Include(j => j.Customer)
            .Where(j =>
                j.SignOffStatus == JobSignOffStatus.SignedOff
                && j.Status != JobStatus.Closed
                && j.Status != JobStatus.Cancelled
                && (j.SignedOffAt == null || j.SignedOffAt <= cutoff))
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return 0;

        var jobIds = jobs.Select(j => j.Id).ToList();
        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.JobId != null && jobIds.Contains(i.JobId.Value))
            .Select(i => new { i.JobId, i.DocumentType, i.Status, i.Total })
            .ToListAsync(ct);

        var billedByJob = invoices
            .Where(i => InvoiceBillingCalculator.CountsTowardJobBilled(i.DocumentType, i.Status))
            .GroupBy(i => i.JobId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        var stuck = jobs
            .Select(j =>
            {
                var billed = billedByJob.GetValueOrDefault(j.Id);
                var unbilled = InvoiceBillingCalculator.CalculateUnbilledResidual(j.QuotedTotal, billed);
                return (Job: j, Billed: billed, Unbilled: unbilled);
            })
            .Where(x => x.Billed <= 0 || x.Unbilled > 0)
            .ToList();

        if (stuck.Count == 0)
            return 0;

        var stuckIds = stuck.Select(x => x.Job.Id).ToList();
        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "collections"
                && n.RelatedEntityType == nameof(Job)
                && n.RelatedEntityId != null
                && stuckIds.Contains(n.RelatedEntityId.Value)
                && n.Title.Contains("still unbilled"))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var row in stuck)
        {
            if (already.Contains(row.Job.Id))
                continue;

            var amount = row.Unbilled > 0 ? row.Unbilled : row.Job.QuotedTotal;
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = row.Job.TenantId,
                Title = $"Job {row.Job.JobNumber} is still unbilled",
                Message = $"{row.Job.Customer?.Name ?? "Customer"} — signed off, R {amount:N0} still to invoice. Raise it from Home or the job Command Center.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = row.Job.Id,
                RelatedEntityType = nameof(Job)
            }, ct);
            created++;
        }

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "UNBILLED_SCAN",
                "Job",
                "stuck-ready-to-invoice",
                $"Created {created} still-unbilled job notification(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunStuckDepositScanAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-2);
        var jobs = await _dbContext.Set<Job>()
            .AsNoTracking()
            .Include(j => j.Customer)
            .Where(j =>
                j.Status != JobStatus.Closed
                && j.Status != JobStatus.Cancelled
                && j.DepositPercent > 0
                && !j.DepositReceived
                && j.CreatedDate <= cutoff)
            .ToListAsync(ct);

        if (jobs.Count == 0)
            return 0;

        var jobIds = jobs.Select(j => j.Id).ToList();
        var raised = (await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i =>
                i.JobId != null
                && jobIds.Contains(i.JobId.Value)
                && i.DocumentType == InvoiceDocumentType.Deposit)
            .Select(i => new { i.JobId, i.DocumentType, i.Status })
            .ToListAsync(ct))
            .Where(i => InvoiceBillingCalculator.CountsTowardJobBilled(i.DocumentType, i.Status))
            .Select(i => i.JobId!.Value)
            .ToHashSet();

        var stuck = jobs.Where(j => !raised.Contains(j.Id)).ToList();
        if (stuck.Count == 0)
            return 0;

        var stuckIds = stuck.Select(j => j.Id).ToList();
        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "collections"
                && n.RelatedEntityType == nameof(Job)
                && n.RelatedEntityId != null
                && stuckIds.Contains(n.RelatedEntityId.Value)
                && n.Title.Contains("still outstanding"))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var job in stuck)
        {
            if (already.Contains(job.Id))
                continue;

            var amount = Math.Round(job.QuotedTotal * job.DepositPercent / 100m, 2);
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = job.TenantId,
                Title = $"Deposit still outstanding on {job.JobNumber}",
                Message = $"{job.Customer?.Name ?? "Customer"} — {job.DepositPercent:N0}% deposit (R {amount:N0}) has not been raised. Raise it from Home so the crew can mobilise.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = job.Id,
                RelatedEntityType = nameof(Job)
            }, ct);
            created++;
        }

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "DEPOSIT_SCAN",
                "Job",
                "stuck-deposits",
                $"Created {created} outstanding-deposit notification(s)",
                ct);
        }

        return created;
    }

    public async Task<int> RunQuoteFollowUpScanAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var horizon = today.AddDays(3);
        var quotes = await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .Where(q =>
                q.Status == QuoteStatus.Sent
                && q.ValidUntil >= today
                && q.ValidUntil <= horizon)
            .ToListAsync(ct);

        if (quotes.Count == 0)
            return 0;

        var ids = quotes.Select(q => q.Id).ToList();
        var converted = (await _dbContext.Set<Job>().AsNoTracking()
            .Where(j => j.QuoteId != null && ids.Contains(j.QuoteId.Value))
            .Select(j => j.QuoteId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "sales"
                && n.RelatedEntityType == nameof(Quote)
                && n.RelatedEntityId != null
                && ids.Contains(n.RelatedEntityId.Value)
                && n.Title.Contains("expires soon"))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var quote in quotes)
        {
            if (converted.Contains(quote.Id) || already.Contains(quote.Id))
                continue;

            var days = (quote.ValidUntil.Date - today).Days;
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = quote.TenantId,
                Title = $"Quote {quote.QuoteNumber} expires soon",
                Message = $"{quote.Customer?.Name ?? "Customer"} — valid until {quote.ValidUntil:yyyy-MM-dd} ({days} day(s)). Follow up before it drops off the convert queue.",
                Category = "sales",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = quote.Id,
                RelatedEntityType = nameof(Quote)
            }, ct);
            created++;
        }

        if (created > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "QUOTE_FOLLOWUP_SCAN",
                "Quote",
                "expiring-quotes",
                $"Created {created} quote follow-up notification(s)",
                ct);
        }

        return created;
    }

    private async Task<int> NotifySlaItemsAsync(
        IEnumerable<(Guid Id, Guid TenantId, string Title, string Message)> items,
        string entityType,
        CancellationToken ct)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return 0;

        var ids = list.Select(i => i.Id).ToList();
        var already = (await _dbContext.Set<TenantNotification>().AsNoTracking()
            .Where(n =>
                n.Category == "approvals"
                && n.RelatedEntityType == entityType
                && n.RelatedEntityId != null
                && ids.Contains(n.RelatedEntityId.Value))
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var item in list)
        {
            if (already.Contains(item.Id))
                continue;

            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = item.TenantId,
                Title = item.Title,
                Message = item.Message,
                Category = "approvals",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = item.Id,
                RelatedEntityType = entityType
            }, ct);
            created++;
        }

        return created;
    }

    private async Task<int> GetSlaHoursAsync(CancellationToken ct)
    {
        var tenantId = _dbContext.CurrentTenantId;
        if (tenantId == Guid.Empty)
            return 48;

        var tenant = await _dbContext.Set<Tenant>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        return tenant is { DefaultApprovalSlaHours: > 0 } ? tenant.DefaultApprovalSlaHours : 48;
    }

    private async Task<int> ScanCompanyDocumentsAsync(DateTime now, CancellationToken ct)
    {
        var docs = await _dbContext.Set<CompanyDocument>()
            .Where(d => !d.NoExpiry && d.ExpiryDate != null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var doc in docs)
        {
            var days = ComplianceExpiryCalculator.GetDaysUntilExpiry(doc.ExpiryDate, now);
            if (days is null) continue;

            var threshold = ComplianceExpiryCalculator.GetAlertThresholdToSend(days.Value, doc.LastExpiryAlertDaysRemaining);
            if (threshold is null) continue;

            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = doc.TenantId,
                Title = $"Company document expiring in {days} day(s)",
                Message = $"{doc.Title} ({doc.DocumentType}) expires on {doc.ExpiryDate:yyyy-MM-dd}. Renew before work is delayed.",
                Category = "compliance",
                TargetRoles = ComplianceRoles,
                RelatedEntityId = doc.Id,
                RelatedEntityType = nameof(CompanyDocument)
            }, ct);

            doc.LastExpiryAlertDaysRemaining = threshold;
            count++;
        }

        if (count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return count;
    }

    private async Task<int> ScanEmployeeCertificationsAsync(DateTime now, CancellationToken ct)
    {
        var certs = await _dbContext.Set<EmployeeCertification>()
            .Include(c => c.Employee)
            .Where(c => !c.NoExpiry && c.ExpiryDate != null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var cert in certs)
        {
            var days = ComplianceExpiryCalculator.GetDaysUntilExpiry(cert.ExpiryDate, now);
            if (days is null) continue;

            var threshold = ComplianceExpiryCalculator.GetAlertThresholdToSend(days.Value, cert.LastExpiryAlertDaysRemaining);
            if (threshold is null) continue;

            var employeeName = cert.Employee != null
                ? $"{cert.Employee.FirstName} {cert.Employee.LastName}"
                : "Employee";

            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = cert.TenantId != Guid.Empty ? cert.TenantId : (cert.Employee?.TenantId ?? Guid.Empty),
                Title = $"Employee certification expiring in {days} day(s)",
                Message = $"{employeeName}: {cert.CertificationType} expires on {cert.ExpiryDate:yyyy-MM-dd}.",
                Category = "compliance",
                TargetRoles = ComplianceRoles,
                RelatedEntityId = cert.Id,
                RelatedEntityType = nameof(EmployeeCertification)
            }, ct);

            cert.LastExpiryAlertDaysRemaining = threshold;
            count++;
        }

        if (count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return count;
    }
}