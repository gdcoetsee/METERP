using METERP.Application.Models;
using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Purchase Order management + receipt into inventory.
/// </summary>
public interface IPurchaseOrderService
{
    Task<PurchaseOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PurchaseOrder>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(PurchaseOrder po, CancellationToken ct = default);
    Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid poId, PurchaseOrderStatus newStatus, CancellationToken ct = default);

    // Line management (similar to Quotes)
    Task<Guid> AddLineAsync(PurchaseOrderLine line, CancellationToken ct = default);
    Task UpdateLineAsync(PurchaseOrderLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Receive the PO (or partial) — updates inventory via StockTransaction (Receipt) and sets status.
    /// For MVP this is a full receive helper; partial can be added later.
    /// </summary>
    Task<Guid> CreateFromRequisitionAsync(Guid requisitionId, Guid supplierId, CancellationToken ct = default);

    /// <summary>
    /// Create GRV. Optional per-line receive quantities (defaults to full outstanding).
    /// When <paramref name="createSkuForFreeTextLines"/> is true, free-text (non-catalog) lines
    /// are promoted to inventory SKUs before stock is received.
    /// </summary>
    Task<GoodsReceiptVoucher?> ReceiveAsync(
        Guid poId,
        Guid receivedByUserId,
        string? supplierDeliveryNote = null,
        IReadOnlyDictionary<Guid, decimal>? lineQuantities = null,
        bool createSkuForFreeTextLines = false,
        CancellationToken ct = default);

    /// <summary>
    /// Promote a free-text (non-catalog) PO line into a stock master SKU.
    /// Links the PO line (and matching requisition line when present), backfills on-hand
    /// for any quantity already received, and carries open reservations onto the new item.
    /// </summary>
    Task<Guid> CreateSkuFromPoLineAsync(
        Guid poLineId,
        string? sku = null,
        string? name = null,
        string? category = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<GoodsReceiptVoucher>> GetRecentGrvsAsync(int take = 50, CancellationToken ct = default);

    Task<IReadOnlyList<GoodsReceiptVoucher>> GetGrvsForPurchaseOrderAsync(Guid poId, CancellationToken ct = default);

    /// <summary>Sent or partially received POs whose expected date has passed.</summary>
    Task<IReadOnlyList<ConvertibleDocumentRow>> GetOverdueQueueAsync(int take = 20, CancellationToken ct = default);

    /// <summary>Draft POs that have lines and can be marked Sent.</summary>
    Task<IReadOnlyList<ConvertibleDocumentRow>> GetUnsentQueueAsync(int take = 20, CancellationToken ct = default);
}
