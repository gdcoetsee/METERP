using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Inventory and stock management for contracting materials.
/// </summary>
public interface IInventoryService
{
    Task<InventoryItem?> GetItemByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<InventoryItem>> GetAllItemsAsync(string? search = null, bool lowStockOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateItemAsync(InventoryItem item, CancellationToken ct = default);
    Task UpdateItemAsync(InventoryItem item, CancellationToken ct = default);

    /// <summary>
    /// Adjust stock and record a transaction (in or out).
    /// </summary>
    Task RecordStockTransactionAsync(Guid itemId, decimal quantityChange, StockTransactionType type, string? reference = null, Guid? jobId = null, string? notes = null, CancellationToken ct = default);

    Task<IReadOnlyList<StockTransaction>> GetTransactionsForItemAsync(Guid itemId, CancellationToken ct = default);

    Task<IReadOnlyList<StockTransaction>> GetRecentTransactionsAsync(int take = 20, CancellationToken ct = default);
}
