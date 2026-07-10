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
        if (string.IsNullOrWhiteSpace(po.PoNumber))
        {
            po.PoNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("PurchaseOrder", "PO", ct)
                : $"PO-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        }

        RecalculateTotals(po);

        _dbContext.Set<PurchaseOrder>().Add(po);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return po.Id;
    }

    public async Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default)
    {
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
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Supplier)
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return;

        var previous = po.Status;
        po.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();

        if (_audit != null)
            await _audit.LogAsync("STATUS", "PurchaseOrder", po.PoNumber, $"{previous} → {newStatus}", ct);

        if (newStatus == PurchaseOrderStatus.Sent && previous != PurchaseOrderStatus.Sent)
        {
            var supplierName = po.Supplier?.Name ?? "supplier";
            var supplierEmail = po.Supplier?.Email?.Trim();
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
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<PurchaseOrderLine>().Add(line);
        await _dbContext.SaveChangesAsync(ct);

        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == line.PurchaseOrderId, ct);
        if (po != null)
        {
            RecalculateTotals(po);
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
        return line.Id;
    }

    public async Task UpdateLineAsync(PurchaseOrderLine line, CancellationToken ct = default)
    {
        // LineTotal is now a computed property on the entity (Quantity * UnitPrice)

        _dbContext.Set<PurchaseOrderLine>().Update(line);
        await _dbContext.SaveChangesAsync(ct);

        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == line.PurchaseOrderId, ct);
        if (po != null)
        {
            RecalculateTotals(po);
            await _dbContext.SaveChangesAsync(ct);
        }

        InvalidateListCaches();
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<PurchaseOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line == null) return;

        var poId = line.PurchaseOrderId;
        line.IsDeleted = true;

        await _dbContext.SaveChangesAsync(ct);

        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po != null)
        {
            RecalculateTotals(po);
            await _dbContext.SaveChangesAsync(ct);
        }

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

        var supplier = await _dbContext.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier == null)
            throw new InvalidOperationException("Supplier not found.");

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

        if (_requisitionService != null)
            await _requisitionService.FulfillAfterPoReceiptAsync(po.Id, ct);

        return grv;
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

    private static void RecalculateTotals(PurchaseOrder po)
    {
        po.Subtotal = po.Lines
            .Where(l => !l.IsDeleted)
            .Sum(l => l.LineTotal);

        po.Tax = Math.Round(po.Subtotal * po.TaxRate, 2);
        po.Total = po.Subtotal + po.Tax;
    }
}
