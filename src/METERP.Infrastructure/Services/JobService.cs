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
    private readonly IAuditService? _audit;

    public JobService(
        AppDbContext dbContext,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        ITenantCacheService? cache = null,
        IDocumentSequenceService? documentSequence = null,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _cache = cache;
        _documentSequence = documentSequence;
        _audit = audit;
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

        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.JobId == jobId && !i.IsDeleted && i.DocumentType != InvoiceDocumentType.CreditNote)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync(ct);

        var billedToDate = invoices.Sum(i => i.Total);
        var actualTotal = job.GetActualTotal();

        return new JobCommandCenterSummary
        {
            JobId = jobId,
            JobNumber = job.JobNumber,
            Title = job.Title,
            Status = job.Status,
            IsClosed = job.IsClosed(),
            QuotedTotal = job.QuotedTotal,
            ActualTotal = actualTotal,
            BilledToDate = billedToDate,
            UnbilledResidual = Math.Max(0m, job.QuotedTotal - billedToDate),
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
            }).ToList(),
            Invoices = invoices.Select(i => new JobInvoiceSummary
            {
                InvoiceId = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                DocumentType = i.DocumentType,
                Status = i.Status,
                Total = i.Total,
                InvoiceDate = i.InvoiceDate
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
                TenantCacheCategories.Jobs,
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
        if (job.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for a job.");
        if (string.IsNullOrWhiteSpace(job.Title))
            throw new InvalidOperationException("Job title is required.");
        job.Title = job.Title.Trim();
        if (job.Title.Length > 200)
            throw new InvalidOperationException("Job title cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(job.Description))
        {
            job.Description = job.Description.Trim();
            if (job.Description.Length > 2000)
                throw new InvalidOperationException("Job description cannot exceed 2000 characters.");
        }
        if (!string.IsNullOrWhiteSpace(job.Notes))
        {
            job.Notes = job.Notes.Trim();
            if (job.Notes.Length > 2000)
                throw new InvalidOperationException("Job notes cannot exceed 2000 characters.");
        }
        if (job.QuotedTotal < 0)
            throw new InvalidOperationException("Quoted total cannot be negative.");
        if (job.QuotedTotal > 100_000_000m)
            throw new InvalidOperationException("Quoted total cannot exceed 100,000,000.");

        var customer = await _dbContext.Set<Customer>().FindAsync([job.CustomerId], ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found.");

        if (job.AssignedEmployeeId is { } leadId && leadId != Guid.Empty)
        {
            var lead = await _dbContext.Set<Employee>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == leadId, ct);
            if (lead == null || lead.IsDeleted)
                throw new InvalidOperationException("Assigned lead employee not found or deleted.");
            if (!lead.IsActive)
                throw new InvalidOperationException("Assigned lead employee not found or inactive.");
        }

        if (job.DivisionId is { } divisionId && divisionId != Guid.Empty)
        {
            var divisionOk = await _dbContext.Set<Division>()
                .AnyAsync(d => d.Id == divisionId && d.IsActive, ct);
            if (!divisionOk)
                throw new InvalidOperationException("Division not found or inactive.");
        }

        if (job.AssetId is { } assetId && assetId != Guid.Empty)
        {
            var asset = await _dbContext.Set<Asset>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assetId, ct);
            if (asset == null || asset.IsDeleted)
                throw new InvalidOperationException("Asset not found or deleted.");
            if (asset.Status == AssetStatus.Decommissioned)
                throw new InvalidOperationException("Cannot assign a decommissioned asset.");
        }

        job.Title = job.Title.Trim();

        var tenantId = _tenantProvider?.GetCurrentTenantId() ?? job.TenantId;
        if (_quotaService != null && tenantId != Guid.Empty)
            await _quotaService.EnsureAllowedAsync(tenantId, QuotaType.Job, ct);

        if (string.IsNullOrWhiteSpace(job.JobNumber))
        {
            job.JobNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("Job", "J", ct)
                : $"J-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        else
        {
            job.JobNumber = job.JobNumber.Trim();
            if (job.JobNumber.Length > 50)
                throw new InvalidOperationException("Job number cannot exceed 50 characters.");
            var numberTaken = await _dbContext.Set<Job>()
                .AnyAsync(j => j.JobNumber == job.JobNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Job number '{job.JobNumber}' already exists.");
        }

        _dbContext.Set<Job>().Add(job);
        await _dbContext.SaveChangesAsync(ct);

        await TryIncrementJobCountAsync(job.TenantId, ct);
        await InvalidateListCachesAsync(ct);

        return job.Id;
    }

    public async Task<Guid> CreateEmergencyAsync(
        Guid customerId,
        string title,
        string? description,
        decimal quotedEstimate,
        decimal depositPercent = 30m,
        decimal retentionPercent = 10m,
        CancellationToken ct = default)
    {
        if (customerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for an emergency job.");
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Title is required.");
        if (quotedEstimate < 0)
            throw new InvalidOperationException("Quoted estimate cannot be negative.");
        if (quotedEstimate > 100_000_000m)
            throw new InvalidOperationException("Quoted estimate cannot exceed 100,000,000.");
        if (depositPercent is < 0 or > 100)
            throw new InvalidOperationException("Deposit % must be between 0 and 100.");
        if (retentionPercent is < 0 or > 100)
            throw new InvalidOperationException("Retention % must be between 0 and 100.");

        var customerExists = await _dbContext.Set<Customer>().AnyAsync(c => c.Id == customerId, ct);
        if (!customerExists)
            throw new InvalidOperationException("Customer not found.");

        var job = new Job
        {
            CustomerId = customerId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            QuotedTotal = quotedEstimate,
            DepositPercent = depositPercent,
            RetentionPercent = retentionPercent,
            IsEmergency = true,
            Status = JobStatus.InProgress,
            Notes = "Emergency / callout job (created without quote)."
        };

        return await CreateAsync(job, ct);
    }

    public async Task UpdateBillingTermsAsync(
        Guid jobId,
        decimal depositPercent,
        decimal retentionPercent,
        CancellationToken ct = default)
    {
        if (depositPercent is < 0 or > 100)
            throw new InvalidOperationException("Deposit % must be between 0 and 100.");
        if (retentionPercent is < 0 or > 100)
            throw new InvalidOperationException("Retention % must be between 0 and 100.");

        var job = await LoadJobForOperationsAsync(jobId, ct);

        if (job.IsClosed() || job.IsCancelled())
            throw new InvalidOperationException("Cannot change billing terms on a closed or cancelled job.");

        job.DepositPercent = depositPercent;
        job.RetentionPercent = retentionPercent;
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(job.Title))
            throw new InvalidOperationException("Job title is required.");
        job.Title = job.Title.Trim();
        if (job.Title.Length > 200)
            throw new InvalidOperationException("Job title cannot exceed 200 characters.");
        if (!string.IsNullOrWhiteSpace(job.Description))
        {
            job.Description = job.Description.Trim();
            if (job.Description.Length > 2000)
                throw new InvalidOperationException("Job description cannot exceed 2000 characters.");
        }
        if (!string.IsNullOrWhiteSpace(job.Notes))
        {
            job.Notes = job.Notes.Trim();
            if (job.Notes.Length > 2000)
                throw new InvalidOperationException("Job notes cannot exceed 2000 characters.");
        }
        if (job.QuotedTotal < 0)
            throw new InvalidOperationException("Quoted total cannot be negative.");
        if (job.QuotedTotal > 100_000_000m)
            throw new InvalidOperationException("Quoted total cannot exceed 100,000,000.");

        var existing = await _dbContext.Set<Job>().AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == job.Id, ct)
            ?? throw new InvalidOperationException("Job not found.");

        if (existing.Status is JobStatus.Closed or JobStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot edit job {existing.JobNumber} — it is {existing.Status}.");

        // Status lifecycle must go through dedicated methods (status / close / cancel / reopen).
        if (job.Status != existing.Status)
            throw new InvalidOperationException(
                "Use status, close, cancel, or reopen actions to change job status.");

        if (job.CustomerId == Guid.Empty)
            job.CustomerId = existing.CustomerId;
        else if (job.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>().FindAsync([job.CustomerId], ct);
            if (customer == null || customer.IsDeleted)
                throw new InvalidOperationException("Customer not found.");
        }

        if (job.AssignedEmployeeId is { } leadId && leadId != Guid.Empty
            && job.AssignedEmployeeId != existing.AssignedEmployeeId)
        {
            var leadOk = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Id == leadId && e.IsActive, ct);
            if (!leadOk)
                throw new InvalidOperationException("Assigned lead employee not found or inactive.");
        }

        if (job.DivisionId is { } divisionId && divisionId != Guid.Empty
            && job.DivisionId != existing.DivisionId)
        {
            var divisionOk = await _dbContext.Set<Division>()
                .AnyAsync(d => d.Id == divisionId && d.IsActive, ct);
            if (!divisionOk)
                throw new InvalidOperationException("Division not found or inactive.");
        }

        if (job.AssetId is { } assetId && assetId != Guid.Empty
            && job.AssetId != existing.AssetId)
        {
            var asset = await _dbContext.Set<Asset>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assetId, ct);
            if (asset == null)
                throw new InvalidOperationException("Asset not found.");
            if (asset.Status == AssetStatus.Decommissioned)
                throw new InvalidOperationException("Cannot assign a decommissioned asset.");
        }

        job.Title = job.Title.Trim();
        if (job.ScheduledStart.HasValue)
        {
            var date = job.ScheduledStart.Value.Date;
            if (date > DateTime.UtcNow.Date.AddYears(2))
                throw new InvalidOperationException("Scheduled start cannot be more than 2 years in the future.");
            if (date < DateTime.UtcNow.Date.AddYears(-1))
                throw new InvalidOperationException("Scheduled start cannot be more than 1 year in the past.");
            job.ScheduledStart = date;
        }

        job.JobNumber = existing.JobNumber;
        job.Status = existing.Status;
        job.ClosedAt = existing.ClosedAt;
        job.ClosedByUserId = existing.ClosedByUserId;
        job.CloseNotes = existing.CloseNotes;

        _dbContext.Set<Job>().Update(job);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task SetCrewAssignmentsAsync(Guid jobId, IReadOnlyList<Guid> employeeIds, CancellationToken ct = default)
    {
        var job = await LoadJobForOperationsAsync(jobId, ct);
        await EnsureJobOpenAsync(job, ct);

        var distinctIds = employeeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count > 0)
        {
            var employees = await _dbContext.Set<Employee>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(e => distinctIds.Contains(e.Id))
                .ToListAsync(ct);
            if (employees.Count != distinctIds.Count)
                throw new InvalidOperationException("One or more crew members are missing or inactive.");
            if (employees.Any(e => e.IsDeleted || !e.IsActive))
                throw new InvalidOperationException("One or more crew members are missing or inactive.");
        }

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
            .Include(j => j.Labors)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job == null) return;

        if (job.Status is JobStatus.Closed)
            throw new InvalidOperationException(
                $"Cannot delete closed job {job.JobNumber}. Keep it for audit history.");

        if (job.Status is not (JobStatus.Scheduled or JobStatus.Cancelled)
            && (job.ActualCosts.Any(c => !c.IsDeleted) || job.Labors.Any(l => !l.IsDeleted)))
            throw new InvalidOperationException(
                $"Cannot delete job {job.JobNumber} with costs or labor. Cancel it instead.");

        var hasInvoices = await _dbContext.Set<Invoice>().AsNoTracking()
            .AnyAsync(i => i.JobId == job.Id, ct);
        if (hasInvoices)
            throw new InvalidOperationException(
                $"Cannot delete job {job.JobNumber} — invoices exist. Cancel the job instead.");

        foreach (var cost in job.ActualCosts)
        {
            cost.IsDeleted = true;
        }
        foreach (var labor in job.Labors)
        {
            labor.IsDeleted = true;
        }
        job.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid jobId, JobStatus newStatus, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return;

        if (job.Status is JobStatus.Closed or JobStatus.Cancelled
            && newStatus is not (JobStatus.Closed or JobStatus.Cancelled))
        {
            throw new InvalidOperationException(
                $"Job {job.JobNumber} is {job.Status}; use Reopen (if closed) or create a new job instead of changing status.");
        }

        if (newStatus == JobStatus.Cancelled)
            throw new InvalidOperationException("Use CancelAsync with a reason to cancel a job.");

        job.Status = newStatus;

        if (newStatus == JobStatus.Completed || newStatus == JobStatus.Invoiced)
        {
            job.CompletedDate = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task<bool> CloseAsync(Guid jobId, Guid executiveUserId, string? notes, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.IsDeleted || job.IsClosed() || job.IsCancelled())
            return false;

        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 500)
            throw new ArgumentException("Close notes cannot exceed 500 characters.", nameof(notes));

        job.Status = JobStatus.Closed;
        job.ClosedAt = DateTime.UtcNow;
        job.ClosedByUserId = executiveUserId;
        job.CloseNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        job.CompletedDate ??= DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "CLOSE",
                "Job",
                job.JobNumber,
                $"Executive close — actual R {job.GetActualTotal():N0}, quoted R {job.QuotedTotal:N0}" +
                (job.CloseNotes != null ? $" — {job.CloseNotes}" : ""),
                ct);
        }

        return true;
    }

    public async Task<bool> ReopenAsync(Guid jobId, Guid executiveUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reopen reason is required.", nameof(reason));
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new ArgumentException("Reopen reason must be at least 3 characters.", nameof(reason));
        if (reason.Length > 500)
            throw new ArgumentException("Reopen reason cannot exceed 500 characters.", nameof(reason));

        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.IsDeleted || !job.IsClosed())
            return false;

        job.Status = JobStatus.Completed;
        job.LastReopenedAt = DateTime.UtcNow;
        job.LastReopenedByUserId = executiveUserId;
        job.LastReopenReason = reason;
        job.ClosedAt = null;
        job.ClosedByUserId = null;
        job.CloseNotes = null;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "REOPEN",
                "Job",
                job.JobNumber,
                $"Executive reopen — {job.LastReopenReason}",
                ct);
        }

        return true;
    }

    public async Task RecalculateActualCostAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null) return;

        job.ActualCost = job.ActualCosts
            .Where(c => !c.IsDeleted)
            .Sum(c => c.Amount);

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task<bool> AdvanceWorkSignOffAsync(Guid jobId, Guid userId, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null)
            return false;

        await EnsureJobOpenAsync(job, ct);

        switch (job.SignOffStatus)
        {
            case JobSignOffStatus.None:
                job.SignOffStatus = JobSignOffStatus.PendingManager;
                await _dbContext.SaveChangesAsync(ct);
                await InvalidateListCachesAsync(ct);
                if (_audit != null)
                    await _audit.LogAsync("SIGNOFF_REQUEST", "Job", job.JobNumber, "Submitted for manager work sign-off", ct);
                return true;

            case JobSignOffStatus.PendingManager: // includes legacy Pending (same value)
                job.SignOffStatus = JobSignOffStatus.PendingExecutive;
                job.ManagerSignedOffAt = DateTime.UtcNow;
                job.ManagerSignedOffByUserId = userId;
                await _dbContext.SaveChangesAsync(ct);
                await InvalidateListCachesAsync(ct);
                if (_audit != null)
                    await _audit.LogAsync("SIGNOFF_MANAGER", "Job", job.JobNumber, "Manager work sign-off approved", ct);
                return true;

            case JobSignOffStatus.PendingExecutive:
                job.SignOffStatus = JobSignOffStatus.SignedOff;
                job.SignedOffAt = DateTime.UtcNow;
                job.SignedOffByUserId = userId;
                if (job.Status is JobStatus.Scheduled or JobStatus.InProgress)
                    job.Status = JobStatus.Completed;
                job.CompletedDate ??= DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(ct);
                await InvalidateListCachesAsync(ct);
                if (_audit != null)
                    await _audit.LogAsync("SIGNOFF_EXECUTIVE", "Job", job.JobNumber, "Executive work sign-off complete", ct);
                return true;

            case JobSignOffStatus.SignedOff:
                return false;

            default:
                return false;
        }
    }

    public async Task<bool> SignOffAsync(Guid jobId, Guid userId, CancellationToken ct = default)
    {
        // Full dual chain complete in one call (demo, E2E, spine tests).
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null)
            return false;

        await EnsureJobOpenAsync(job, ct);

        if (job.SignOffStatus != JobSignOffStatus.SignedOff)
        {
            if (job.ManagerSignedOffAt == null)
            {
                job.ManagerSignedOffAt = DateTime.UtcNow;
                job.ManagerSignedOffByUserId = userId;
            }

            job.SignOffStatus = JobSignOffStatus.SignedOff;
            job.SignedOffAt = DateTime.UtcNow;
            job.SignedOffByUserId = userId;

            if (job.Status is JobStatus.Scheduled or JobStatus.InProgress)
                job.Status = JobStatus.Completed;

            job.CompletedDate ??= DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
            await InvalidateListCachesAsync(ct);

            if (_audit != null)
                await _audit.LogAsync("SIGNOFF_COMPLETE", "Job", job.JobNumber, "Full work sign-off (manager+executive)", ct);
        }

        return true;
    }

    public async Task<bool> CancelAsync(Guid jobId, Guid userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new ArgumentException("Cancellation reason must be at least 3 characters.", nameof(reason));
        if (reason.Length > 500)
            throw new ArgumentException("Cancellation reason cannot exceed 500 characters.", nameof(reason));

        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.IsDeleted)
            return false;

        if (job.IsClosed())
            throw new InvalidOperationException($"Job {job.JobNumber} is closed; reopen before cancelling if needed.");

        if (job.IsCancelled())
            return false;

        job.Status = JobStatus.Cancelled;
        job.CancelledAt = DateTime.UtcNow;
        job.CancelledByUserId = userId;
        job.CancellationReason = reason;
        job.CompletedDate ??= DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "CANCEL",
                "Job",
                job.JobNumber,
                job.CancellationReason,
                ct);
        }

        return true;
    }

    public async Task<Guid> AddCostAsync(JobCost cost, CancellationToken ct = default)
    {
        if (cost.Amount < 0)
            throw new InvalidOperationException("Cost amount cannot be negative.");
        if (cost.Amount > 10_000_000m)
            throw new InvalidOperationException("Cost amount cannot exceed 10,000,000.");

        cost.Description = string.IsNullOrWhiteSpace(cost.Description)
            ? "Job cost"
            : cost.Description.Trim();
        if (cost.Description.Length > 500)
            throw new InvalidOperationException("Cost description cannot exceed 500 characters.");
        if (string.IsNullOrWhiteSpace(cost.CostType))
            cost.CostType = "Other";
        else
            cost.CostType = cost.CostType.Trim();
        if (cost.CostType.Length > 50)
            throw new InvalidOperationException("Cost type cannot exceed 50 characters.");

        cost.CostDate = cost.CostDate == default ? DateTime.UtcNow.Date : cost.CostDate.Date;
        if (cost.CostDate > DateTime.UtcNow.Date.AddDays(1))
            throw new InvalidOperationException("Cost date cannot be more than one day in the future.");
        if (cost.CostDate < DateTime.UtcNow.Date.AddYears(-2))
            throw new InvalidOperationException("Cost date cannot be more than 2 years in the past.");

        var job = await LoadJobForOperationsAsync(cost.JobId, ct);

        await EnsureJobOpenAsync(job, ct);

        _dbContext.Set<JobCost>().Add(cost);
        await _dbContext.SaveChangesAsync(ct);
        await RecalculateActualCostAsync(cost.JobId, ct);
        return cost.Id;
    }

    public async Task DeleteCostAsync(Guid costId, CancellationToken ct = default)
    {
        var cost = await _dbContext.Set<JobCost>().FirstOrDefaultAsync(c => c.Id == costId, ct);
        if (cost == null) return;

        var jobId = cost.JobId;
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job != null)
            await EnsureJobOpenAsync(job, ct);

        cost.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        await RecalculateActualCostAsync(jobId, ct);
    }

    public async Task<Guid> AddLaborAsync(JobLabor labor, CancellationToken ct = default)
    {
        if (labor.Hours <= 0)
            throw new InvalidOperationException("Labor hours must be positive.");
        if (labor.Hours > 24m)
            throw new InvalidOperationException("Labor hours cannot exceed 24 in a single entry.");
        if (labor.HourlyRate < 0)
            throw new InvalidOperationException("Hourly rate cannot be negative.");
        if (labor.HourlyRate > 50_000m)
            throw new InvalidOperationException("Hourly rate cannot exceed 50,000.");
        if (!string.IsNullOrWhiteSpace(labor.Description) && labor.Description.Trim().Length > 500)
            throw new InvalidOperationException("Labor description cannot exceed 500 characters.");

        labor.WorkDate = labor.WorkDate == default ? DateTime.UtcNow.Date : labor.WorkDate.Date;
        if (labor.WorkDate > DateTime.UtcNow.Date.AddDays(1))
            throw new InvalidOperationException("Labor work date cannot be more than one day in the future.");
        if (labor.WorkDate < DateTime.UtcNow.Date.AddYears(-2))
            throw new InvalidOperationException("Labor work date cannot be more than 2 years in the past.");

        var job = await LoadJobForOperationsAsync(labor.JobId, ct);

        await EnsureJobOpenAsync(job, ct);
        await ApplyEmployeeDefaultsAsync(labor, ct);

        if (labor.Hours <= 0)
            throw new InvalidOperationException("Labor hours must be positive.");

        if (labor.EmployeeId is { } empId && empId != Guid.Empty)
        {
            var empOk = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Id == empId && e.IsActive, ct);
            if (!empOk)
                throw new InvalidOperationException("Labor employee not found or inactive.");
        }

        if (!string.IsNullOrWhiteSpace(labor.Technician))
        {
            labor.Technician = labor.Technician.Trim();
            if (labor.Technician.Length > 200)
                throw new InvalidOperationException("Technician name cannot exceed 200 characters.");
        }

        if (!string.IsNullOrWhiteSpace(labor.Description))
            labor.Description = labor.Description.Trim();

        _dbContext.Set<JobLabor>().Add(labor);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
        return labor.Id;
    }

    public async Task DeleteLaborAsync(Guid laborId, CancellationToken ct = default)
    {
        var labor = await _dbContext.Set<JobLabor>().FirstOrDefaultAsync(l => l.Id == laborId, ct);
        if (labor == null) return;

        var jobId = labor.JobId;
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job != null)
            await EnsureJobOpenAsync(job, ct);

        labor.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    private async Task<Job> LoadJobForOperationsAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.IsDeleted)
            throw new InvalidOperationException($"Job {jobId} was not found or deleted.");
        return job;
    }

    private Task EnsureJobOpenAsync(Job job, CancellationToken ct)
    {
        if (job.IsDeleted)
            throw new InvalidOperationException($"Job {job.JobNumber} is deleted.");
        if (job.IsOpenForOperations())
            return Task.CompletedTask;

        throw JobClosedException.ForJob(job.JobNumber);
    }

    private Task InvalidateListCachesAsync(CancellationToken ct) =>
        _cache == null
            ? Task.CompletedTask
            : TenantCacheInvalidation.OnJobMutatedAsync(_cache, ct);

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
        if (string.IsNullOrWhiteSpace(milestone.Title))
            throw new InvalidOperationException("Milestone title is required.");

        milestone.Title = milestone.Title.Trim();
        if (milestone.Title.Length > 200)
            throw new InvalidOperationException("Milestone title cannot exceed 200 characters.");

        var job = await LoadJobForOperationsAsync(milestone.JobId, ct);
        await EnsureJobOpenAsync(job, ct);

        _dbContext.Set<JobMilestone>().Add(milestone);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
        return milestone.Id;
    }

    public async Task UpdateMilestoneAsync(JobMilestone milestone, CancellationToken ct = default)
    {
        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == milestone.JobId, ct)
            ?? throw new InvalidOperationException("Job not found.");
        await EnsureJobOpenAsync(job, ct);

        if (string.IsNullOrWhiteSpace(milestone.Title))
            throw new InvalidOperationException("Milestone title is required.");
        milestone.Title = milestone.Title.Trim();
        if (milestone.Title.Length > 200)
            throw new InvalidOperationException("Milestone title cannot exceed 200 characters.");

        _dbContext.Set<JobMilestone>().Update(milestone);
        await _dbContext.SaveChangesAsync(ct);
        await InvalidateListCachesAsync(ct);
    }

    public async Task DeleteMilestoneAsync(Guid milestoneId, CancellationToken ct = default)
    {
        var milestone = await _dbContext.Set<JobMilestone>().FirstOrDefaultAsync(m => m.Id == milestoneId, ct);
        if (milestone == null) return;

        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == milestone.JobId, ct);
        if (job != null)
            await EnsureJobOpenAsync(job, ct);

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
        if (string.IsNullOrWhiteSpace(snag.Description))
            throw new InvalidOperationException("Snag description is required.");

        snag.Description = snag.Description.Trim();
        if (snag.Description.Length > 2000)
            throw new InvalidOperationException("Snag description cannot exceed 2000 characters.");

        var job = await LoadJobForOperationsAsync(snag.JobId, ct);
        await EnsureJobOpenAsync(job, ct);

        _dbContext.Set<JobSnagItem>().Add(snag);
        await _dbContext.SaveChangesAsync(ct);
        return snag.Id;
    }

    public async Task ResolveSnagAsync(Guid snagId, Guid userId, CancellationToken ct = default)
    {
        var snag = await _dbContext.Set<JobSnagItem>().FirstOrDefaultAsync(s => s.Id == snagId, ct);
        if (snag == null || snag.IsResolved) return;

        var job = await _dbContext.Set<Job>().FirstOrDefaultAsync(j => j.Id == snag.JobId, ct);
        if (job != null)
            await EnsureJobOpenAsync(job, ct);

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
        if (string.IsNullOrWhiteSpace(incident.Description))
            throw new InvalidOperationException("Safety incident description is required.");

        incident.Description = incident.Description.Trim();
        if (incident.Description.Length > 2000)
            throw new InvalidOperationException("Safety incident description cannot exceed 2000 characters.");

        // Safety logs remain allowed on closed jobs for post-incident compliance capture,
        // but not on soft-deleted jobs.
        if (incident.JobId != Guid.Empty)
        {
            var job = await _dbContext.Set<Job>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == incident.JobId, ct);
            if (job == null || job.IsDeleted)
                throw new InvalidOperationException("Job not found or deleted.");
        }

        _dbContext.Set<JobSafetyIncident>().Add(incident);
        await _dbContext.SaveChangesAsync(ct);
        return incident.Id;
    }

    public async Task CloseSafetyIncidentAsync(Guid incidentId, Guid userId, string? correctiveAction, CancellationToken ct = default)
    {
        var incident = await _dbContext.Set<JobSafetyIncident>().FirstOrDefaultAsync(i => i.Id == incidentId, ct);
        if (incident == null || incident.IsClosed) return;
        if (!string.IsNullOrWhiteSpace(correctiveAction))
        {
            correctiveAction = correctiveAction.Trim();
            if (correctiveAction.Length > 2000)
                throw new InvalidOperationException("Corrective action cannot exceed 2000 characters.");
            incident.CorrectiveAction = correctiveAction;
        }
        incident.IsClosed = true;
        incident.ClosedAt = DateTime.UtcNow;
        incident.ClosedByUserId = userId;
        await _dbContext.SaveChangesAsync(ct);
    }
}
