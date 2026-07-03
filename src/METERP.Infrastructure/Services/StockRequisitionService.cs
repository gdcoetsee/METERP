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

    public StockRequisitionService(
        AppDbContext dbContext,
        IInventoryService inventoryService,
        IDocumentSequenceService? documentSequence = null,
        IAuditService? audit = null,
        IPpeIssueService? ppeIssue = null)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _documentSequence = documentSequence;
        _audit = audit;
        _ppeIssue = ppeIssue;
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

        if (requisition.Lines == null || !requisition.Lines.Any(l => l.QuantityRequested > 0))
            throw new InvalidOperationException("At least one line with quantity is required.");

        foreach (var line in requisition.Lines)
        {
            if (line.QuantityRequested <= 0)
                throw new InvalidOperationException("Line quantity must be positive.");

            var item = await _dbContext.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == line.InventoryItemId, ct);
            if (item == null || !item.IsActive)
                throw new InvalidOperationException("Inventory item not found or inactive.");
        }

        requisition.Status = RequisitionStatus.PendingManager;
        requisition.RequisitionNumber = _documentSequence != null
            ? await _documentSequence.GetNextNumberAsync("Requisition", "REQ", ct)
            : $"REQ-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        var lines = requisition.Lines.ToList();
        requisition.Lines = new List<StockRequisitionLine>();
        _dbContext.Set<StockRequisition>().Add(requisition);
        await _dbContext.SaveChangesAsync(ct);

        foreach (var line in lines)
        {
            line.StockRequisitionId = requisition.Id;
            line.QuantityReserved = 0;
            line.QuantityIssued = 0;
            _dbContext.Set<StockRequisitionLine>().Add(line);
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            await _audit.LogAsync(
                "SUBMIT",
                "StockRequisition",
                requisition.RequisitionNumber,
                $"Submitted for job, {lines.Count} line(s)" + (requisition.IsPpe ? " [PPE]" : ""),
                ct);
        }

        return requisition.Id;
    }

    public async Task<bool> ApproveManagerAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status != RequisitionStatus.PendingManager)
            return false;

        req.Status = RequisitionStatus.PendingExecutive;
        req.ManagerApprovedByUserId = approverUserId;
        req.ManagerApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("APPROVE_MANAGER", req, ct);
        return true;
    }

    public async Task<bool> ApproveExecutiveAsync(Guid requisitionId, Guid approverUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status != RequisitionStatus.PendingExecutive)
            return false;

        var anyReserved = false;
        var anyShort = false;

        foreach (var line in req.Lines.Where(l => !l.IsDeleted))
        {
            var item = await _dbContext.Set<InventoryItem>().FirstAsync(i => i.Id == line.InventoryItemId, ct);
            var available = StockAvailabilityCalculator.GetAvailableQuantity(item.QuantityOnHand, item.QuantityReserved);
            var reserve = StockAvailabilityCalculator.CalculateReservation(line.QuantityRequested, available);

            line.QuantityReserved = reserve;
            if (reserve > 0)
            {
                item.QuantityReserved += reserve;
                anyReserved = true;
            }

            if (reserve < line.QuantityRequested)
                anyShort = true;
        }

        req.Status = anyShort && !anyReserved
            ? RequisitionStatus.AwaitingProcurement
            : RequisitionStatus.Approved;

        req.ExecutiveApprovedByUserId = approverUserId;
        req.ExecutiveApprovedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var detail = anyShort
            ? "Executive approved — partial/no stock reserved; procurement may be required"
            : "Executive approved — stock reserved for issue";
        if (req.IsPpe)
            detail += " [PPE]";

        if (_audit != null)
            await _audit.LogAsync("APPROVE_EXECUTIVE", "StockRequisition", req.RequisitionNumber, detail, ct);

        return true;
    }

    public async Task<bool> RejectAsync(Guid requisitionId, Guid approverUserId, string reason, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status is RequisitionStatus.Issued or RequisitionStatus.Rejected or RequisitionStatus.Cancelled)
            return false;

        await ReleaseReservationsAsync(req, ct);
        req.Status = RequisitionStatus.Rejected;
        req.RejectionReason = reason;
        req.LastModifiedBy = approverUserId.ToString();
        await _dbContext.SaveChangesAsync(ct);
        await LogAsync("REJECT", req, reason, ct);
        return true;
    }

    public async Task<bool> IssueAsync(Guid requisitionId, Guid issuedByUserId, CancellationToken ct = default)
    {
        var req = await LoadForUpdateAsync(requisitionId, ct);
        if (req == null || req.Status != RequisitionStatus.Approved)
            return false;

        var issuedAny = false;
        foreach (var line in req.Lines.Where(l => !l.IsDeleted && l.QuantityReserved > 0))
        {
            var toIssue = line.QuantityReserved - line.QuantityIssued;
            if (toIssue <= 0) continue;

            var item = await _dbContext.Set<InventoryItem>().FirstAsync(i => i.Id == line.InventoryItemId, ct);
            item.QuantityReserved = Math.Max(0, item.QuantityReserved - toIssue);

            await _inventoryService.RecordStockTransactionAsync(
                line.InventoryItemId,
                -toIssue,
                StockTransactionType.Issue,
                req.RequisitionNumber,
                req.JobId,
                $"Issued from requisition {req.RequisitionNumber}",
                ct);

            line.QuantityIssued += toIssue;
            issuedAny = true;

            _dbContext.Set<JobCost>().Add(new JobCost
            {
                JobId = req.JobId,
                Description = $"{item.Name} (req {req.RequisitionNumber})",
                Amount = toIssue * item.UnitCost,
                CostType = "Material",
                CostDate = DateTime.UtcNow
            });
        }

        if (!issuedAny)
            return false;

        req.Status = RequisitionStatus.Issued;
        req.IssuedByUserId = issuedByUserId;
        req.IssuedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        if (req.IsPpe && _ppeIssue != null)
            await _ppeIssue.RecordFromRequisitionIssueAsync(req, ct);

        await LogAsync("ISSUE", req, "Stock issued to job", ct);
        return true;
    }

    public async Task<bool> FulfillAfterPoReceiptAsync(Guid purchaseOrderId, CancellationToken ct = default)
    {
        var req = await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PurchaseOrderId == purchaseOrderId, ct);

        if (req == null || req.Status is not (RequisitionStatus.AwaitingProcurement or RequisitionStatus.ProcurementOrdered))
            return false;

        var anyReserved = false;
        foreach (var line in req.Lines.Where(l => !l.IsDeleted))
        {
            var shortfall = line.QuantityRequested - line.QuantityReserved;
            if (shortfall <= 0) continue;

            var item = await _dbContext.Set<InventoryItem>().FirstAsync(i => i.Id == line.InventoryItemId, ct);
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
        await LogAsync("PO_RECEIVED", req, "Stock reserved after GRV — ready for issue", ct);
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
            var item = await _dbContext.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == line.InventoryItemId, ct);
            if (item != null)
                item.QuantityReserved = Math.Max(0, item.QuantityReserved - release);
            line.QuantityReserved = line.QuantityIssued;
        }
    }

    private async Task LogAsync(string action, StockRequisition req, CancellationToken ct) =>
        await LogAsync(action, req, null, ct);

    private async Task LogAsync(string action, StockRequisition req, string? details, CancellationToken ct)
    {
        if (_audit == null) return;
        await _audit.LogAsync(action, "StockRequisition", req.RequisitionNumber, details ?? action, ct);
    }
}