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
    Task ReceiveAsync(Guid poId, CancellationToken ct = default);
}
