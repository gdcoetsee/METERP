using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly AppDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IStockRequisitionService? _requisitionService;
    private readonly IDocumentSequenceService? _documentSequence;
    private readonly IAuditService? _audit;
    private readonly ITenantCacheService? _cache;
    private readonly ITenantNotificationService? _notifications;
    private readonly IEmailSender? _email;

    public PurchaseOrderService(
        AppDbContext dbContext,
        IInventoryService inventoryService,
        IStockRequisitionService? requisitionService = null,
        IDocumentSequenceService? documentSequence = null,
        IAuditService? audit = null,
        ITenantCacheService? cache = null,
        ITenantNotificationService? notifications = null,
        IEmailSender? email = null)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _requisitionService = requisitionService;
        _documentSequence = documentSequence;
        _audit = audit;
        _cache = cache;
        _notifications = notifications;
        _email = email;
    }

    public async Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<PurchaseOrder>()
            .Include(po => po.Lines)
            .Include(po => po.Supplier)
            .FirstOrDefaultAsync(po => po.Id == id, ct);
    }

    public async Task<IReadOnlyList<PurchaseOrder>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.PurchaseOrders,
                $"p{page}:s{pageSize}",
                () => LoadPurchaseOrdersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadPurchaseOrdersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<PurchaseOrder>> LoadPurchaseOrdersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .Include(po => po.Supplier)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(po =>
                po.PoNumber.ToLower().Contains(term) ||
                (po.Notes != null && po.Notes.ToLower().Contains(term)) ||
                (po.Supplier != null && po.Supplier.Name.ToLower().Contains(term)));
        }

        return await query
            .OrderByDescending(po => po.PoDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        if (po.SupplierId == Guid.Empty)
            throw new InvalidOperationException("Supplier is required for a purchase order.");

        // FindAsync sees tracked (unsaved) suppliers used by unit tests and the same request.
        var supplier = await _dbContext.Set<Supplier>().FindAsync([po.SupplierId], ct);
        if (supplier == null || supplier.IsDeleted)
            throw new InvalidOperationException("Supplier not found.");
        if (!supplier.IsActive)
            throw new InvalidOperationException("Supplier is inactive.");

        if (po.TaxRate < 0 || po.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");

        if (po.ExpectedDate.HasValue && po.PoDate != default
            && po.ExpectedDate.Value.Date < po.PoDate.Date)
            throw new InvalidOperationException("Expected delivery date cannot be before the PO date.");
        if (po.ExpectedDate.HasValue && po.ExpectedDate.Value.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Expected delivery date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(po.Notes))
        {
            po.Notes = po.Notes.Trim();
            if (po.Notes.Length > 2000)
                throw new InvalidOperationException("Purchase order notes cannot exceed 2000 characters.");
        }

        if (string.IsNullOrWhiteSpace(po.PoNumber))
        {
            po.PoNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("PurchaseOrder", "PO", ct)
                : $"PO-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }
        else
        {
            po.PoNumber = po.PoNumber.Trim();
            if (po.PoNumber.Length > 50)
                throw new InvalidOperationException("Purchase order number cannot exceed 50 characters.");
            var numberTaken = await _dbContext.Set<PurchaseOrder>()
                .AnyAsync(p => p.PoNumber == po.PoNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Purchase order number '{po.PoNumber}' already exists.");
        }

        foreach (var line in po.Lines.Where(l => !l.IsDeleted))
        {
            ValidateLine(line);
            await EnsureInventoryItemForLineAsync(line, ct);
        }

        RecalculateTotals(po);

        _dbContext.Set<PurchaseOrder>().Add(po);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return po.Id;
    }

    public async Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<PurchaseOrder>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == po.Id, ct);
        if (existing == null)
            throw new InvalidOperationException("Purchase order not found.");
        if (existing.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException(
                $"Cannot edit purchase order in status {existing.Status}. Only Draft POs can be edited.");

        if (po.SupplierId == Guid.Empty)
            throw new InvalidOperationException("Supplier is required for a purchase order.");
        var supplier = await _dbContext.Set<Supplier>().FindAsync([po.SupplierId], ct);
        if (supplier == null || supplier.IsDeleted)
            throw new InvalidOperationException("Supplier not found.");

        if (po.TaxRate < 0 || po.TaxRate > 1m)
            throw new InvalidOperationException("Tax rate must be between 0 and 1 (e.g. 0.15 for 15%).");
        if (po.ExpectedDate.HasValue && po.PoDate != default
            && po.ExpectedDate.Value.Date < po.PoDate.Date)
            throw new InvalidOperationException("Expected delivery date cannot be before the PO date.");
        if (po.ExpectedDate.HasValue && po.ExpectedDate.Value.Date > DateTime.UtcNow.Date.AddYears(2))
            throw new InvalidOperationException("Expected delivery date cannot be more than 2 years in the future.");
        if (!string.IsNullOrWhiteSpace(po.Notes))
        {
            po.Notes = po.Notes.Trim();
            if (po.Notes.Length > 2000)
                throw new InvalidOperationException("Purchase order notes cannot exceed 2000 characters.");
        }

        // Preserve identity fields that must not drift via free-form update payloads.
        po.PoNumber = existing.PoNumber;
        po.Status = existing.Status;

        RecalculateTotals(po);
        _dbContext.Set<PurchaseOrder>().Update(po);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (po == null) return;

        if (po.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled))
            throw new InvalidOperationException(
                $"Cannot delete PO in status {po.Status}. Only Draft or Cancelled POs can be deleted.");

        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived
            || po.Lines.Any(l => !l.IsDeleted && l.QuantityReceived > 0))
            throw new InvalidOperationException("Cannot delete a PO that has received goods.");

        foreach (var line in po.Lines)
        {
            line.IsDeleted = true;
        }
        po.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task UpdateStatusAsync(Guid poId, PurchaseOrderStatus newStatus, CancellationToken ct = default)
    {
        // Do not Include Supplier here: a required + filtered navigation hides the PO
        // when the supplier is soft-deleted (same pattern as invoice credit notes).
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return;

        var previous = po.Status;
        if (previous == newStatus) return;

        if (previous == PurchaseOrderStatus.Cancelled)
            throw new InvalidOperationException("Cancelled POs cannot change status.");

        if (previous == PurchaseOrderStatus.Received && newStatus != PurchaseOrderStatus.Received)
            throw new InvalidOperationException("Fully received POs cannot change status.");

        if (newStatus == PurchaseOrderStatus.Cancelled
            && previous is PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)
            throw new InvalidOperationException("Cannot cancel a PO that has already been received.");

        if (newStatus == PurchaseOrderStatus.Sent && previous != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only draft POs can be marked Sent.");

        if (newStatus == PurchaseOrderStatus.Sent
            && !po.Lines.Any(l => !l.IsDeleted))
            throw new InvalidOperationException("Cannot send a purchase order with no lines.");

        Supplier? supplierForSend = null;
        if (newStatus == PurchaseOrderStatus.Sent)
        {
            supplierForSend = await _dbContext.Set<Supplier>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == po.SupplierId, ct);
            if (supplierForSend == null || supplierForSend.IsDeleted)
                throw new InvalidOperationException("Cannot send a purchase order — supplier is missing or deleted.");
            if (!supplierForSend.IsActive)
                throw new InvalidOperationException("Cannot send a purchase order — supplier is missing or inactive.");
        }

        // Received/PartiallyReceived should come from GRV ReceiveAsync, not manual flip.
        if (newStatus is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived
            && previous is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
            throw new InvalidOperationException("Use GRV receive to mark goods as received.");

        po.Status = newStatus;

        // Cancelling a PO unhooks linked requisitions so procurement can re-quote.
        if (newStatus == PurchaseOrderStatus.Cancelled)
        {
            var linkedReqs = await _dbContext.Set<StockRequisition>()
                .Where(r => r.PurchaseOrderId == po.Id
                    && r.Status == RequisitionStatus.ProcurementOrdered)
                .ToListAsync(ct);
            foreach (var req in linkedReqs)
            {
                req.PurchaseOrderId = null;
                req.Status = RequisitionStatus.AwaitingProcurement;
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_audit != null)
            await _audit.LogAsync("STATUS", "PurchaseOrder", po.PoNumber, $"{previous} → {newStatus}", ct);

        if (newStatus == PurchaseOrderStatus.Sent && previous != PurchaseOrderStatus.Sent)
        {
            var supplierName = supplierForSend?.Name ?? "supplier";
            var supplierEmail = supplierForSend?.Email?.Trim();
            var emailSent = false;

            if (_email?.IsConfigured == true && !string.IsNullOrWhiteSpace(supplierEmail))
            {
                var lines = po.Lines
                    .Where(l => !l.IsDeleted)
                    .Select(l => (l.Description, l.Quantity, l.Unit ?? "ea", l.UnitPrice));
                var html = PurchaseOrderEmailBuilder.BuildHtml(
                    po.PoNumber, supplierName, po.Total, po.ExpectedDate, lines);
                await _email.SendEmailAsync(supplierEmail, $"Purchase Order {po.PoNumber}", html, ct);
                emailSent = true;

                if (_audit != null)
                    await _audit.LogAsync("EMAIL", "PurchaseOrder", po.PoNumber, $"E-PO sent to {supplierEmail}", ct);
            }

            if (_notifications != null)
            {
                var emailNote = emailSent
                    ? $"E-PO emailed to {supplierEmail}."
                    : string.IsNullOrWhiteSpace(supplierEmail)
                        ? "Add supplier email to enable outbound PO email."
                        : "SMTP not configured — PO marked sent in-system only.";

                await _notifications.CreateAsync(new TenantNotification
                {
                    Title = "PO sent to supplier",
                    Message = $"{po.PoNumber} marked sent to {supplierName}. {emailNote}",
                    Category = "procurement",
                    TargetRoles = "Admin,Executive,Procurement",
                    RelatedEntityId = po.Id,
                    RelatedEntityType = "PurchaseOrder"
                }, ct);
            }
        }
    }

    public async Task<Guid> AddLineAsync(PurchaseOrderLine line, CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == line.PurchaseOrderId, ct)
            ?? throw new InvalidOperationException("Purchase order not found.");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Lines can only be added to draft purchase orders.");

        ValidateLine(line);
        await EnsureInventoryItemForLineAsync(line, ct);

        _dbContext.Set<PurchaseOrderLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        await _dbContext.Entry(po).Collection(p => p.Lines).LoadAsync(ct);
        RecalculateTotals(po);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(PurchaseOrderLine line, CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == line.PurchaseOrderId, ct)
            ?? throw new InvalidOperationException("Purchase order not found.");

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Lines can only be edited on draft purchase orders.");

        ValidateLine(line);
        await EnsureInventoryItemForLineAsync(line, ct);

        _dbContext.Set<PurchaseOrderLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        RecalculateTotals(po);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<PurchaseOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var poId = line.PurchaseOrderId;
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return;

        if (po.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Lines can only be removed from draft purchase orders.");

        line.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);

        RecalculateTotals(po);
        await _dbContext.SaveChangesAsync(ct);

        InvalidateListCaches();
    }

    public async Task<Guid> CreateFromRequisitionAsync(Guid requisitionId, Guid supplierId, CancellationToken ct = default)
    {
        var req = await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines).ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == requisitionId, ct);

        if (req == null)
            throw new InvalidOperationException("Requisition not found.");

        if (req.Status is not (RequisitionStatus.AwaitingProcurement or RequisitionStatus.ProcurementOrdered))
            throw new InvalidOperationException("Requisition is not awaiting procurement.");

        if (req.PurchaseOrderId.HasValue)
            throw new InvalidOperationException("A purchase order already exists for this requisition.");

        var job = await _dbContext.Set<Job>().AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j =>
                j.Id == req.JobId
                && (req.TenantId == Guid.Empty || j.TenantId == req.TenantId), ct);
        if (job == null || job.IsDeleted)
            throw new InvalidOperationException("Job not found or deleted for this requisition.");
        if (!job.IsOpenForOperations())
            throw JobClosedException.ForJob(job.JobNumber);

        var supplier = await _dbContext.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier == null || !supplier.IsActive)
            throw new InvalidOperationException("Supplier not found or inactive.");

        var po = new PurchaseOrder
        {
            SupplierId = supplierId,
            PoDate = DateTime.UtcNow,
            ExpectedDate = DateTime.UtcNow.AddDays(7),
            Status = PurchaseOrderStatus.Draft,
            TaxRate = 0.15m,
            Notes = $"From requisition {req.RequisitionNumber}"
        };

        foreach (var line in req.Lines.Where(l => !l.IsDeleted))
        {
            var toOrder = line.QuantityRequested - line.QuantityReserved;
            if (toOrder <= 0) continue;

            po.Lines.Add(new PurchaseOrderLine
            {
                InventoryItemId = line.IsNonCatalog ? null : line.InventoryItemId,
                Description = line.IsNonCatalog
                    ? line.DisplayDescription
                    : (line.InventoryItem?.Name ?? line.DisplayDescription),
                Quantity = toOrder,
                UnitPrice = line.IsNonCatalog
                    ? line.EstimatedUnitCost
                    : (line.InventoryItem?.UnitCost ?? 0m),
                Unit = line.Unit ?? line.InventoryItem?.Unit ?? "ea"
            });
        }

        if (!po.Lines.Any())
            throw new InvalidOperationException("No shortfall quantity to order.");

        await CreateAsync(po, ct);

        req.PurchaseOrderId = po.Id;
        req.Status = RequisitionStatus.ProcurementOrdered;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("CREATE_FROM_REQ", "PurchaseOrder", po.PoNumber,
                $"Created from {req.RequisitionNumber}", ct);

        if (_notifications != null)
        {
            await _notifications.CreateAsync(new TenantNotification
            {
                Title = "Procurement PO created",
                Message = $"{po.PoNumber} drafted for {req.RequisitionNumber} — mark sent then receive via GRV.",
                Category = "procurement",
                TargetRoles = "Admin,Executive,Procurement",
                RelatedEntityId = po.Id,
                RelatedEntityType = "PurchaseOrder"
            }, ct);
        }

        return po.Id;
    }

    public async Task<GoodsReceiptVoucher?> ReceiveAsync(
        Guid poId,
        Guid receivedByUserId,
        string? supplierDeliveryNote = null,
        IReadOnlyDictionary<Guid, decimal>? lineQuantities = null,
        bool createSkuForFreeTextLines = false,
        CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return null;

        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            return null;

        if (po.Status is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived))
            throw new InvalidOperationException("PO must be Sent before receiving (GRV).");

        if (!string.IsNullOrWhiteSpace(supplierDeliveryNote))
        {
            supplierDeliveryNote = supplierDeliveryNote.Trim();
            if (supplierDeliveryNote.Length > 100)
                throw new InvalidOperationException("Supplier delivery note cannot exceed 100 characters.");
        }

        if (lineQuantities != null)
        {
            foreach (var qty in lineQuantities.Values)
            {
                if (qty < 0)
                    throw new InvalidOperationException("Receive quantity cannot be negative.");
                if (qty > 1_000_000m)
                    throw new InvalidOperationException("Receive quantity cannot exceed 1,000,000.");
            }
        }

        if (createSkuForFreeTextLines)
        {
            foreach (var freeText in po.Lines.Where(l => !l.IsDeleted && !l.InventoryItemId.HasValue))
            {
                var outstanding = freeText.QuantityOutstanding;
                if (lineQuantities != null && lineQuantities.TryGetValue(freeText.Id, out var rq) && rq <= 0)
                    continue;
                if (outstanding <= 0 && freeText.QuantityReceived <= 0)
                    continue;

                await CreateSkuFromPoLineAsync(freeText.Id, ct: ct);
            }

            // Reload lines so InventoryItemId is current for receipt posting.
            po = await _dbContext.Set<PurchaseOrder>()
                .Include(p => p.Lines)
                .FirstAsync(p => p.Id == poId, ct);
        }

        var linkedRequisitionId = await _dbContext.Set<StockRequisition>()
            .Where(r => r.PurchaseOrderId == po.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        var grv = new GoodsReceiptVoucher
        {
            GrvNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("GRV", "GRV", ct)
                : $"GRV-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            PurchaseOrderId = po.Id,
            StockRequisitionId = linkedRequisitionId,
            ReceivedByUserId = receivedByUserId,
            ReceivedAt = DateTime.UtcNow,
            SupplierDeliveryNote = string.IsNullOrWhiteSpace(supplierDeliveryNote)
                ? null
                : supplierDeliveryNote.Trim()
        };

        _dbContext.Set<GoodsReceiptVoucher>().Add(grv);
        await _dbContext.SaveChangesAsync(ct);

        var receivedAny = false;
        foreach (var line in po.Lines.Where(l => !l.IsDeleted))
        {
            var outstanding = line.QuantityOutstanding;
            if (outstanding <= 0) continue;

            var qty = outstanding;
            if (lineQuantities != null && lineQuantities.TryGetValue(line.Id, out var requestedQty))
            {
                if (requestedQty <= 0) continue;
                qty = Math.Min(outstanding, requestedQty);
            }

            if (line.InventoryItemId.HasValue)
            {
                await _inventoryService.RecordStockTransactionAsync(
                    line.InventoryItemId.Value,
                    qty,
                    StockTransactionType.Receipt,
                    grv.GrvNumber,
                    null,
                    $"GRV {grv.GrvNumber} — PO {po.PoNumber}: {line.Description}",
                    ct);
            }

            _dbContext.Set<GoodsReceiptLine>().Add(new GoodsReceiptLine
            {
                GoodsReceiptVoucherId = grv.Id,
                PurchaseOrderLineId = line.Id,
                InventoryItemId = line.InventoryItemId,
                QuantityReceived = qty
            });

            line.QuantityReceived += qty;
            receivedAny = true;
        }

        if (!receivedAny)
        {
            grv.IsDeleted = true;
            await _dbContext.SaveChangesAsync(ct);
            return null;
        }

        var allReceived = po.Lines.Where(l => !l.IsDeleted).All(l => l.QuantityReceived >= l.Quantity);
        po.Status = allReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_audit != null)
        {
            var note = $"PO {po.PoNumber}" +
                       (grv.SupplierDeliveryNote != null ? $" — DN {grv.SupplierDeliveryNote}" : "");
            await _audit.LogAsync("RECEIVE", "GRV", grv.GrvNumber, note, ct);
        }

        if (_notifications != null)
        {
            var statusWord = allReceived ? "fully received" : "partially received";
            await _notifications.CreateAsync(new TenantNotification
            {
                TenantId = po.TenantId,
                Title = $"GRV {grv.GrvNumber} against {po.PoNumber}",
                Message = $"{po.PoNumber} is {statusWord}. Stock is on hand — issue to jobs or reserve for requisitions.",
                Category = "procurement",
                TargetRoles = "Admin,Executive,Procurement,Stores",
                RelatedEntityId = po.Id,
                RelatedEntityType = nameof(PurchaseOrder)
            }, ct);
        }

        if (_requisitionService != null)
            await _requisitionService.FulfillAfterPoReceiptAsync(po.Id, ct);

        return grv;
    }

    public async Task<Guid> CreateSkuFromPoLineAsync(
        Guid poLineId,
        string? sku = null,
        string? name = null,
        string? category = null,
        CancellationToken ct = default)
    {
        var line = await _dbContext.Set<PurchaseOrderLine>()
            .Include(l => l.PurchaseOrder)
            .FirstOrDefaultAsync(l => l.Id == poLineId, ct)
            ?? throw new InvalidOperationException("Purchase order line not found.");

        if (line.InventoryItemId.HasValue && line.InventoryItemId != Guid.Empty)
            throw new InvalidOperationException("Line is already linked to an inventory SKU.");

        if (string.IsNullOrWhiteSpace(line.Description))
            throw new InvalidOperationException("Line needs a description to create a SKU.");

        var itemName = string.IsNullOrWhiteSpace(name) ? line.Description.Trim() : name.Trim();
        var item = new InventoryItem
        {
            Sku = string.IsNullOrWhiteSpace(sku) ? string.Empty : sku.Trim(),
            Name = itemName,
            Description = line.Description.Trim(),
            Unit = string.IsNullOrWhiteSpace(line.Unit) ? "ea" : line.Unit!,
            UnitCost = line.UnitPrice,
            Category = string.IsNullOrWhiteSpace(category) ? "Non-catalog" : category.Trim(),
            QuantityOnHand = 0m,
            QuantityReserved = 0m,
            ReorderLevel = 0m,
            IsActive = true
        };

        var itemId = await _inventoryService.CreateItemAsync(item, ct);
        line.InventoryItemId = itemId;
        await _dbContext.SaveChangesAsync(ct);

        // Backfill stock for qty already received without a catalog link.
        if (line.QuantityReceived > 0)
        {
            var grvRef = line.PurchaseOrder?.PoNumber ?? "PO";
            await _inventoryService.RecordStockTransactionAsync(
                itemId,
                line.QuantityReceived,
                StockTransactionType.Receipt,
                grvRef,
                null,
                $"SKU created from free-text PO line; backfill received qty for {line.Description}",
                ct);
        }

        // Patch historical GRV lines that had no inventory link.
        var grvLines = await _dbContext.Set<GoodsReceiptLine>()
            .Where(g => g.PurchaseOrderLineId == poLineId && g.InventoryItemId == null)
            .ToListAsync(ct);
        foreach (var gl in grvLines)
            gl.InventoryItemId = itemId;

        // Link matching free-text requisition lines and move open reservations onto the SKU.
        var req = await _dbContext.Set<StockRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PurchaseOrderId == line.PurchaseOrderId, ct);

        if (req != null)
        {
            var desc = line.Description.Trim();
            foreach (var rl in req.Lines.Where(l => !l.IsDeleted && l.IsNonCatalog))
            {
                var rlDesc = rl.DisplayDescription.Trim();
                if (!string.Equals(rlDesc, desc, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals((rl.Description ?? string.Empty).Trim(), desc, StringComparison.OrdinalIgnoreCase))
                    continue;

                rl.InventoryItemId = itemId;
                if (string.IsNullOrWhiteSpace(rl.Unit))
                    rl.Unit = line.Unit;

                var reservedUnissued = rl.QuantityReserved - rl.QuantityIssued;
                if (reservedUnissued > 0)
                {
                    var inv = await _dbContext.Set<InventoryItem>().FirstAsync(i => i.Id == itemId, ct);
                    inv.QuantityReserved += reservedUnissued;
                }

                break;
            }
        }

        if (grvLines.Count > 0 || req != null)
            await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
        {
            var created = await _inventoryService.GetItemByIdAsync(itemId, ct);
            await _audit.LogAsync(
                "CREATE_SKU",
                "InventoryItem",
                created?.Sku ?? itemId.ToString("N")[..8],
                $"From free-text PO line: {line.Description}",
                ct);
        }

        InvalidateListCaches();
        return itemId;
    }

    public async Task<IReadOnlyList<GoodsReceiptVoucher>> GetRecentGrvsAsync(int take = 50, CancellationToken ct = default)
    {
        return await _dbContext.Set<GoodsReceiptVoucher>()
            .AsNoTracking()
            .Include(g => g.PurchaseOrder).ThenInclude(p => p!.Supplier)
            .Include(g => g.Lines)
            .OrderByDescending(g => g.ReceivedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GoodsReceiptVoucher>> GetGrvsForPurchaseOrderAsync(Guid poId, CancellationToken ct = default)
    {
        return await _dbContext.Set<GoodsReceiptVoucher>()
            .AsNoTracking()
            .Include(g => g.Lines)
            .Where(g => g.PurchaseOrderId == poId)
            .OrderByDescending(g => g.ReceivedAt)
            .ToListAsync(ct);
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.PurchaseOrders);

    private static void ValidateLine(PurchaseOrderLine line)
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
    }

    /// <summary>
    /// When InventoryItemId is set, it must reference a live active SKU. Free-text lines omit the FK.
    /// </summary>
    private async Task EnsureInventoryItemForLineAsync(PurchaseOrderLine line, CancellationToken ct)
    {
        if (line.InventoryItemId is null || line.InventoryItemId == Guid.Empty)
        {
            line.InventoryItemId = null;
            return;
        }

        var item = await _dbContext.Set<InventoryItem>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == line.InventoryItemId.Value, ct);
        if (item == null || item.IsDeleted)
            throw new InvalidOperationException("Inventory item not found or deleted.");
        if (!item.IsActive)
            throw new InvalidOperationException("Inventory item is inactive.");
    }

    private static void RecalculateTotals(PurchaseOrder po)
    {
        po.Subtotal = po.Lines
            .Where(l => !l.IsDeleted)
            .Sum(l => l.LineTotal);

        po.Tax = Math.Round(po.Subtotal * po.TaxRate, 2);
        po.Total = po.Subtotal + po.Tax;
    }
}
