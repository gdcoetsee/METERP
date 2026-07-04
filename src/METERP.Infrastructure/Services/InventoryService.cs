using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public InventoryService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<InventoryItem?> GetItemByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<InventoryItem>()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<InventoryItem>> GetAllItemsAsync(string? search = null, bool lowStockOnly = false, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Inventory,
                $"p{page}:s{pageSize}:low{(lowStockOnly ? 1 : 0)}",
                () => LoadItemsAsync(search, lowStockOnly, page, pageSize, ct),
                ct: ct);
        }

        return await LoadItemsAsync(search, lowStockOnly, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<InventoryItem>> LoadItemsAsync(string? search, bool lowStockOnly, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(i => i.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i =>
                i.Name.ToLower().Contains(term) ||
                i.Sku.ToLower().Contains(term) ||
                (i.Category != null && i.Category.ToLower().Contains(term)));
        }

        if (lowStockOnly)
        {
            query = query.Where(i => i.QuantityOnHand <= i.ReorderLevel);
        }

        return await query
            .OrderBy(i => i.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateItemAsync(InventoryItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.Sku))
        {
            item.Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
        }

        _dbContext.Set<InventoryItem>().Add(item);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return item.Id;
    }

    public async Task UpdateItemAsync(InventoryItem item, CancellationToken ct = default)
    {
        _dbContext.Set<InventoryItem>().Update(item);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task RecordStockTransactionAsync(Guid itemId, decimal quantityChange, StockTransactionType type, string? reference = null, Guid? jobId = null, string? notes = null, CancellationToken ct = default)
    {
        var item = await _dbContext.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item == null) return;

        // Update on-hand
        item.QuantityOnHand += quantityChange;

        var transaction = new StockTransaction
        {
            InventoryItemId = itemId,
            Type = type,
            Quantity = quantityChange,
            UnitCostAtTime = item.UnitCost,
            Reference = reference,
            JobId = jobId,
            Notes = notes
        };

        _dbContext.Set<StockTransaction>().Add(transaction);

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task<IReadOnlyList<StockTransaction>> GetTransactionsForItemAsync(Guid itemId, CancellationToken ct = default)
    {
        return await _dbContext.Set<StockTransaction>()
            .AsNoTracking()
            .Where(t => t.InventoryItemId == itemId)
            .OrderByDescending(t => t.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockTransaction>> GetRecentTransactionsAsync(int take = 20, CancellationToken ct = default)
    {
        return await _dbContext.Set<StockTransaction>()
            .AsNoTracking()
            .Include(t => t.InventoryItem)
            .OrderByDescending(t => t.CreatedDate)
            .Take(take)
            .ToListAsync(ct);
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory(TenantCacheCategories.Inventory);
}