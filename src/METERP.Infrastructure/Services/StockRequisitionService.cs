using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class StockRequisitionService : IStockRequisitionService
{
    private readonly AppDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IDocumentSequenceService? _documentSequence;
    private readonly IAuditService? _audit;
    private readonly IPpeIssueService? _ppeIssue;
    private readonly IJobService? _jobService;
    private readonly ITenantNotificationService? _notifications;

    public StockRequisitionService(
        AppDbContext dbContext,
        IInventoryService inventoryService,
        IDocumentSequenceService? documentSequence = null,
        IAuditService? audit = null,
        IPpeIssueService? ppeIssue = null,
        IJobService? jobService = null,
        ITenantNotificationService? notifications = null)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _documentSequence = documentSequence;
        _audit = audit;
        _ppeIssue = ppeIssue;
        _jobService = jobService;
        _notifications = notifications;
    }

    public async Task<StockRequisition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines).ThenInclude(l => l.InventoryItem)
            .Include(r => r.Job)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<StockRequisition>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.PurchaseOrder)
            .Where(r => r.JobId == jobId)
            .OrderByDescending(r => r.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockRequisition>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Job)
            .Include(r => r.Lines).ThenInclude(l => l.InventoryItem)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(r =>
                r.RequisitionNumber.ToLower().Contains(term) ||
                (r.Job != null && r.Job.JobNumber.ToLower().Contains(term)) ||
                (r.Notes != null && r.Notes.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(r => r.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockRequisition>> GetPendingApprovalsAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Job)
            .Include(r => r.Lines).ThenInclude(l => l.InventoryItem)
            .Where(r => r.Status == RequisitionStatus.PendingManager
                || r.Status == RequisitionStatus.PendingExecutive)
            .OrderBy(r => r.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<Guid> SubmitAsync(StockRequisition requisition, CancellationToken ct = default)
    {
        if (requisition.JobId == Guid.Empty)
            throw new InvalidOperationException("Job is required for a stock requisition.");

        // Soft-delete aware so deleted jobs are not reported as a vague "not found".
        var job = await _dbContext.Set<Job>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == requisition.JobId, ct);
        if (job == null || job.IsDeleted)
            throw new InvalidOperationException("Job not found or deleted.");
        if (!job.IsOpenForOperations())
            throw JobClosedException.ForJob(job.JobNumber);

        if (requisition.Lines == null || !requisition.Lines.Any(l => l.QuantityRequested > 0))
            throw new InvalidOperationException("At least one line with quantity is required.");

        var lines = requisition.Lines.Where(l => l.QuantityRequested > 0).ToList();
        foreach (var line in lines)
        {
            if (line.QuantityRequested <= 0)
                throw new InvalidOperationException("Line quantity must be positive.");
            if (line.QuantityRequested > 1_000_000m)
                throw new InvalidOperationException("Line quantity cannot exceed 1,000,000.");

            var hasItem = line.InventoryItemId.HasValue && line.InventoryItemId != Guid.Empty;
            if (hasItem)
            {
                var item = await _dbContext.Set<InventoryItem>()
                    .FirstOrDefaultAsync(i => i.Id == line.InventoryItemId!.Value, ct);
                if (item == null || !item.IsActive)
                    throw new InvalidOperationException("Inventory item not found or inactive.");
                if (string.IsNullOrWhiteSpace(line.Description))
                    line.Description = item.Name;
                if (string.IsNullOrWhiteSpace(line.Unit))
                    line.Unit = item.Unit;
            }
            else
            {
                line.InventoryItemId = null;
                if (string.IsNullOrWhiteSpace(line.Description))
                    throw new InvalidOperationException(
                        "Non-catalog lines require a description (item not yet in stock master).");
                line.Description = line.Description.Trim();
                if (line.Description.Length > 500)
                    throw new InvalidOperationException("Line description cannot exceed 500 characters.");
            }

            if (line.EstimatedUnitCost < 0)
                throw new InvalidOperationException("Estimated unit cost cannot be negative.");
            if (line.EstimatedUnitCost > 10_000_000m)
                throw new InvalidOperationException("Estimated unit cost cannot exceed 10,000,000.");
            if (!string.IsNullOrWhiteSpace(line.Unit))
            {
                line.Unit = line.Unit.Trim();
                if (line.Unit.Length > 20)
                    throw new InvalidOperationException("Line unit cannot exceed 20 characters.");
            }
        }

        requisition.Status = RequisitionStatus.PendingManager;
        if (requisition.TenantId == Guid.Empty)
            requisition.TenantId = job.TenantId;
        requisition.RequisitionNumber = _documentSequence != null
            ? await _documentSequence.GetNextNumberAsync("Requisition", "REQ", ct)
            : $"REQ-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        requisition.Lines = new List<StockRequisitionLine>();
        _dbContext.Set<StockRequisition>().Add(requisition);
        await _dbContext.SaveChangesAsync(ct);

        var nonCatalogCount = 0;
        foreach (var line in lines)
        {
            line.StockRequisitionId = requisition.Id;
            line.QuantityReserved = 0;
            line.QuantityIssued = 0;
            if (line.IsNonCatalog)
                nonCatalogCount++;
            _dbContext.Set<StockRequisitionLine>().Add(line);
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "SUBMIT",
                "StockRequisition",
                requisition.RequisitionNumber,
                $"Submitted for job, {lines.Count} line(s)" +
                (nonCatalogCount > 0 ? $", {nonCatalogCount} non-catalog" : "") +
                (requisition.IsPpe ? " [PPE]" : ""),
                ct);
        }

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = requisition.TenantId,
                Title = $"{requisition.RequisitionNumber} needs manager approval",
                Message = $"{job.JobNumber}: {lines.Count} line(s) waiting. Approve so stores can reserve or procurement can raise a PO.",
                Category = "procurement",
                TargetRoles = "Admin,Executive,Division Manager",
                RelatedEntityId = requisition.Id,
                RelatedEntityType = nameof(StockRequisition)
            }, ct);
        }

        return requisition.Id;
    }

    public async Task<bool> ApproveManagerAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status != RequisitionStatus.PendingManager)
            return false;

        await EnsureJobOpenForRequisitionAsync(req, ct);

        req.Status = RequisitionStatus.PendingExecutive;
        req.ManagerApprovedByUserId = approverUserId;
        req.ManagerApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("APPROVE_MANAGER", req, ct);

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = req.TenantId,
                Title = $"{req.RequisitionNumber} needs executive approval",
                Message = $"{req.RequisitionNumber} passed manager review. Executive approve so stock can be reserved or a PO raised.",
                Category = "procurement",
                TargetRoles = "Admin,Executive",
                RelatedEntityId = req.Id,
                RelatedEntityType = nameof(StockRequisition)
            }, ct);
        }

        return true;
    }

    public async Task<bool> ApproveExecutiveAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status != RequisitionStatus.PendingExecutive)
            return false;

        await EnsureJobOpenForRequisitionAsync(req, ct);

        var anyShort = false;

        foreach (var line in req.Lines.Where(l => !l.IsDeleted))
        {
            if (line.IsNonCatalog)
            {
                // Free-text needs always require procurement (cannot reserve from stock master).
                line.QuantityReserved = 0;
                anyShort = true;
                continue;
            }

            var item = await _dbContext.Set<InventoryItem>()
                .FirstOrDefaultAsync(i => i.Id == line.InventoryItemId!.Value, ct);
            if (item == null || !item.IsActive)
                throw new InvalidOperationException(
                    "Cannot approve requisition — a catalog line references a missing or inactive inventory item. Cancel or reject and re-submit.");

            var available = StockAvailabilityCalculator.GetAvailableQuantity(item.QuantityOnHand, item.QuantityReserved);
            var reserve = StockAvailabilityCalculator.CalculateReservation(line.QuantityRequested, available);

            line.QuantityReserved = reserve;
            if (reserve > 0)
                item.QuantityReserved += reserve;

            if (reserve < line.QuantityRequested)
                anyShort = true;
        }

        req.Status = anyShort
            ? RequisitionStatus.AwaitingProcurement
            : RequisitionStatus.Approved;

        req.ExecutiveApprovedByUserId = approverUserId;
        req.ExecutiveApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var detail = anyShort
            ? "Executive approved — shortfall and/or non-catalog lines require procurement"
            : "Executive approved — stock fully reserved for issue";
        if (req.IsPpe)
            detail += " [PPE]";

        if (_audit != null)
            await _audit.LogAsync("APPROVE_EXECUTIVE", "StockRequisition", req.RequisitionNumber, detail, ct);

        if (_notifications != null)
        {
            if (anyShort)
            {
                await _notifications.CreateAsync(new TenantNotification
                {
                    TenantId = req.TenantId,
                    Title = $"{req.RequisitionNumber} needs a purchase order",
                    Message = $"{req.RequisitionNumber} is approved with a stock shortfall. Raise a PO so the job is not blocked.",
                    Category = "procurement",
                    TargetRoles = "Admin,Executive,Procurement",
                    RelatedEntityId = req.Id,
                    RelatedEntityType = nameof(StockRequisition)
                }, ct);
            }
            else
            {
                await _notifications.CreateAsync(new TenantNotification
                {
                    TenantId = req.TenantId,
                    Title = $"{req.RequisitionNumber} is reserved — issue stock",
                    Message = $"{req.RequisitionNumber} is fully reserved. Issue it to the job from Approvals or Stores.",
                    Category = "procurement",
                    TargetRoles = "Admin,Executive,Stores",
                    RelatedEntityId = req.Id,
                    RelatedEntityType = nameof(StockRequisition)
                }, ct);
            }
        }

        return true;
    }

    public async Task<bool> RejectAsync(Guid requisitionId, Guid approverUserId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.", nameof(reason));
        reason = reason.Trim();
        if (reason.Length < 3)
            throw new ArgumentException("Rejection reason must be at least 3 characters.", nameof(reason));
        if (reason.Length > 500)
            throw new ArgumentException("Rejection reason cannot exceed 500 characters.", nameof(reason));

        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status is RequisitionStatus.Issued or RequisitionStatus.Rejected or RequisitionStatus.Cancelled)
            return false;

        await ReleaseReservationsAsync(req, ct);
        req.Status = RequisitionStatus.Rejected;
        req.RejectionReason = reason;
        req.LastModifiedBy = approverUserId.ToString();
        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("REJECT", req, req.RejectionReason, ct);

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = req.TenantId,
                Title = $"{req.RequisitionNumber} was rejected",
                Message = $"{req.RequisitionNumber}: {reason}",
                Category = "procurement",
                TargetRoles = "Technician,Admin,Executive",
                RelatedEntityId = req.Id,
                RelatedEntityType = nameof(StockRequisition)
            }, ct);
        }

        return true;
    }

    public async Task<bool> CancelAsync(Guid requisitionId, Guid userId, string? reason = null, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null)
            return false;

        if (req.Status is RequisitionStatus.Issued or RequisitionStatus.Cancelled or RequisitionStatus.Rejected)
            return false;

        // Do not cancel after goods have been partially issued.
        if (req.Lines.Any(l => !l.IsDeleted && l.QuantityIssued > 0))
            throw new InvalidOperationException(
                "Cannot cancel a requisition that already has issued stock. Complete or reverse issues first.");

        // Linked open POs must be cancelled first so procurement does not orphan supplier orders.
        if (req.PurchaseOrderId is { } poId && poId != Guid.Empty)
        {
            var po = await _dbContext.Set<PurchaseOrder>().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == poId, ct);
            if (po != null && po.Status != PurchaseOrderStatus.Cancelled)
                throw new InvalidOperationException(
                    $"Cannot cancel requisition {req.RequisitionNumber} — purchase order {po.PoNumber} is still open. Cancel the PO first.");
        }

        await ReleaseReservationsAsync(req, ct);
        req.Status = RequisitionStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length > 500)
            throw new ArgumentException("Cancellation reason cannot exceed 500 characters.", nameof(reason));

        req.RejectionReason = string.IsNullOrWhiteSpace(reason)
            ? "Cancelled"
            : reason.Trim();
        req.LastModifiedBy = userId.ToString();
        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("CANCEL", req, req.RejectionReason, ct);
        return true;
    }

    public async Task<bool> IssueAsync(Guid requisitionId, Guid issuedByUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status is not (
            RequisitionStatus.Approved
            or RequisitionStatus.AwaitingProcurement
            or RequisitionStatus.ProcurementOrdered))
            return false;

        // Soft-delete aware open check (same pattern as approvals / field reports).
        await EnsureJobOpenForRequisitionAsync(req, ct);

        var issuedAny = false;
        var postedAmount = 0m;
        foreach (var line in req.Lines.Where(l => !l.IsDeleted && l.QuantityReserved > 0))
        {
            var toIssue = line.QuantityReserved - line.QuantityIssued;
            if (toIssue <= 0) continue;

            if (line.IsNonCatalog)
            {
                // Non-catalog: procurement fulfilled (reserved after GRV) — post job cost, no stock txn.
                line.QuantityIssued += toIssue;
                issuedAny = true;
                var unitCost = line.EstimatedUnitCost;
                var amount = toIssue * unitCost;
                postedAmount += amount;
                _dbContext.Set<JobCost>().Add(new JobCost
                {
                    JobId = req.JobId,
                    Description = $"{line.DisplayDescription} (req {req.RequisitionNumber}, non-catalog)",
                    Amount = amount,
                    CostType = "Material",
                    CostDate = DateTime.UtcNow
                });
                continue;
            }

            var item = await _dbContext.Set<InventoryItem>().FirstAsync(i => i.Id == line.InventoryItemId!.Value, ct);
            item.QuantityReserved = Math.Max(0, item.QuantityReserved - toIssue);

            await _inventoryService.RecordStockTransactionAsync(
                line.InventoryItemId!.Value,
                -toIssue,
                StockTransactionType.Issue,
                req.RequisitionNumber,
                req.JobId,
                $"Issued from requisition {req.RequisitionNumber}",
                ct);

            line.QuantityIssued += toIssue;
            issuedAny = true;
            var catalogAmount = toIssue * item.UnitCost;
            postedAmount += catalogAmount;

            _dbContext.Set<JobCost>().Add(new JobCost
            {
                JobId = req.JobId,
                Description = $"{item.Name} (req {req.RequisitionNumber})",
                Amount = catalogAmount,
                CostType = "Material",
                CostDate = DateTime.UtcNow
            });
        }

        if (!issuedAny)
            return false;

        var fullyIssued = req.Lines
            .Where(l => !l.IsDeleted)
            .All(l => l.QuantityIssued >= l.QuantityRequested);

        if (fullyIssued)
        {
            req.Status = RequisitionStatus.Issued;
            req.IssuedByUserId = issuedByUserId;
            req.IssuedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_jobService != null)
            await _jobService.RecalculateActualCostAsync(req.JobId, ct);

        if (req.IsPpe && _ppeIssue != null)
            await _ppeIssue.RecordFromRequisitionIssueAsync(req, ct);

        await LogAsync("ISSUE", req, "Stock issued to job", ct);

        if (_notifications != null)
        {
            var jobNumber = await _dbContext.Set<Job>().AsNoTracking()
                .Where(j => j.Id == req.JobId)
                .Select(j => j.JobNumber)
                .FirstOrDefaultAsync(ct) ?? "job";

            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = req.TenantId,
                Title = $"Stock issued to {jobNumber}",
                Message = $"{req.RequisitionNumber} posted R {postedAmount:N0} of materials. Complete sign-off to invoice the job.",
                Category = "collections",
                TargetRoles = "Admin,Executive,Finance",
                RelatedEntityId = req.JobId,
                RelatedEntityType = nameof(Job)
            }, ct);
        }

        return true;
    }

    public async Task<bool> FulfillAfterPoReceiptAsync(Guid purchaseOrderId, CancellationToken ct = default)
    {
        var req = await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PurchaseOrderId == purchaseOrderId, ct);

        if (req == null || req.Status is not (RequisitionStatus.AwaitingProcurement or RequisitionStatus.ProcurementOrdered))
            return false;

        // Do not reserve stock against a closed/cancelled job — IssueAsync would refuse and leave trapped reservations.
        await EnsureJobOpenForRequisitionAsync(req, ct);

        var anyReserved = false;
        foreach (var line in req.Lines.Where(l => !l.IsDeleted))
        {
            var shortfall = line.QuantityRequested - line.QuantityReserved;
            if (shortfall <= 0) continue;

            if (line.IsNonCatalog)
            {
                // Free-text lines are fulfilled when the PO is received (no stock master yet).
                line.QuantityReserved = line.QuantityRequested;
                anyReserved = true;
                continue;
            }

            var item = await _dbContext.Set<InventoryItem>()
                .FirstOrDefaultAsync(i => i.Id == line.InventoryItemId!.Value, ct);
            if (item == null || !item.IsActive)
                throw new InvalidOperationException(
                    "Cannot fulfill requisition after GRV — a catalog line references a missing or inactive inventory item.");

            var available = StockAvailabilityCalculator.GetAvailableQuantity(item.QuantityOnHand, item.QuantityReserved);
            var reserve = StockAvailabilityCalculator.CalculateReservation(shortfall, available);

            if (reserve <= 0) continue;

            line.QuantityReserved += reserve;
            item.QuantityReserved += reserve;
            anyReserved = true;
        }

        if (!anyReserved)
            return false;

        var fullyReserved = req.Lines.Where(l => !l.IsDeleted)
            .All(l => l.QuantityReserved >= l.QuantityRequested);

        if (fullyReserved)
            req.Status = RequisitionStatus.Approved;

        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("PO_RECEIVED", req, "Stock/non-catalog reserved after GRV — ready for issue", ct);
        return true;
    }

    private async Task<StockRequisition?> LoadForUpdateAsync(Guid id, CancellationToken ct)
    {
        return await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    private async Task ReleaseReservationsAsync(StockRequisition req, CancellationToken ct)
    {
        foreach (var line in req.Lines.Where(l => !l.IsDeleted && l.QuantityReserved > l.QuantityIssued))
        {
            var release = line.QuantityReserved - line.QuantityIssued;
            if (!line.IsNonCatalog && line.InventoryItemId.HasValue)
            {
                var item = await _dbContext.Set<InventoryItem>()
                    .FirstOrDefaultAsync(i => i.Id == line.InventoryItemId.Value, ct);
                if (item != null)
                    item.QuantityReserved = Math.Max(0, item.QuantityReserved - release);
            }

            line.QuantityReserved = line.QuantityIssued;
        }
    }

    private async Task EnsureJobOpenForRequisitionAsync(StockRequisition req, CancellationToken ct)
    {
        // Ignore soft-delete so deleted jobs surface as an explicit integrity error (not "not found").
        var job = await _dbContext.Set<Job>().AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j =>
                j.Id == req.JobId
                && (req.TenantId == Guid.Empty || j.TenantId == req.TenantId), ct);

        if (job == null || job.IsDeleted)
            throw new InvalidOperationException("Job not found or deleted for this requisition.");
        if (!job.IsOpenForOperations())
            throw JobClosedException.ForJob(job.JobNumber);
    }

    private async Task LogAsync(string action, StockRequisition req, CancellationToken ct) =>
        await LogAsync(action, req, null, ct);

    private async Task LogAsync(string action, StockRequisition req, string? details, CancellationToken ct)
    {
        if (_audit == null) return;
        await _audit.LogAsync(action, "StockRequisition", req.RequisitionNumber, details ?? action, ct);
    }
}