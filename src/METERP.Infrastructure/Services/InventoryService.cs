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
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new InvalidOperationException("Inventory item name is required.");

        item.Name = item.Name.Trim();
        if (item.Name.Length > 200)
            throw new InvalidOperationException("Inventory item name cannot exceed 200 characters.");

        if (string.IsNullOrWhiteSpace(item.Sku))
        {
            item.Sku = "SKU-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
        }
        else
        {
            item.Sku = item.Sku.Trim().ToUpperInvariant();
            if (item.Sku.Length > 50)
                throw new InvalidOperationException("SKU cannot exceed 50 characters.");
            var dup = await _dbContext.Set<InventoryItem>()
                .AnyAsync(i => i.Sku == item.Sku, ct);
            if (dup)
                throw new InvalidOperationException($"SKU '{item.Sku}' already exists.");
        }

        if (item.UnitCost < 0)
            throw new InvalidOperationException("Unit cost cannot be negative.");
        if (item.QuantityOnHand < 0)
            throw new InvalidOperationException("Opening quantity cannot be negative.");
        if (item.QuantityOnHand > 1_000_000m)
            throw new InvalidOperationException("Opening quantity cannot exceed 1,000,000.");
        if (item.ReorderLevel < 0)
            throw new InvalidOperationException("Reorder level cannot be negative.");
        if (item.ReorderLevel > 1_000_000m)
            throw new InvalidOperationException("Reorder level cannot exceed 1,000,000.");
        if (item.UnitCost > 1_000_000m)
            throw new InvalidOperationException("Unit cost cannot exceed 1,000,000.");
        if (!string.IsNullOrWhiteSpace(item.Unit))
        {
            item.Unit = item.Unit.Trim();
            if (item.Unit.Length > 20)
                throw new InvalidOperationException("Unit cannot exceed 20 characters.");
        }
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            item.Category = item.Category.Trim();
            if (item.Category.Length > 100)
                throw new InvalidOperationException("Category cannot exceed 100 characters.");
        }

        _dbContext.Set<InventoryItem>().Add(item);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return item.Id;
    }

    public async Task UpdateItemAsync(InventoryItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new InvalidOperationException("Inventory item name is required.");
        item.Name = item.Name.Trim();
        if (item.Name.Length > 200)
            throw new InvalidOperationException("Inventory item name cannot exceed 200 characters.");
        if (item.UnitCost < 0)
            throw new InvalidOperationException("Unit cost cannot be negative.");
        if (item.ReorderLevel < 0)
            throw new InvalidOperationException("Reorder level cannot be negative.");
        if (item.ReorderLevel > 1_000_000m)
            throw new InvalidOperationException("Reorder level cannot exceed 1,000,000.");
        if (item.UnitCost > 1_000_000m)
            throw new InvalidOperationException("Unit cost cannot exceed 1,000,000.");
        if (!string.IsNullOrWhiteSpace(item.Unit))
        {
            item.Unit = item.Unit.Trim();
            if (item.Unit.Length > 20)
                throw new InvalidOperationException("Unit cannot exceed 20 characters.");
        }
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            item.Category = item.Category.Trim();
            if (item.Category.Length > 100)
                throw new InvalidOperationException("Category cannot exceed 100 characters.");
        }

        // Do not allow direct QuantityOnHand edits via Update — use stock transactions.
        var existing = await _dbContext.Set<InventoryItem>().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == item.Id, ct)
            ?? throw new InvalidOperationException("Inventory item not found.");

        if (!item.IsActive && existing.IsActive)
        {
            if (existing.QuantityReserved > 0)
                throw new InvalidOperationException(
                    "Cannot deactivate an item with reserved stock. Release reservations first.");

            var openReqLines = await (
                from line in _dbContext.Set<StockRequisitionLine>().AsNoTracking()
                join req in _dbContext.Set<StockRequisition>().AsNoTracking()
                    on line.StockRequisitionId equals req.Id
                where line.InventoryItemId == item.Id
                      && !line.IsDeleted
                      && req.Status != RequisitionStatus.Issued
                      && req.Status != RequisitionStatus.Cancelled
                      && req.Status != RequisitionStatus.Rejected
                select line.Id).AnyAsync(ct);

            if (openReqLines)
                throw new InvalidOperationException(
                    "Cannot deactivate an item on open stock requisitions. Complete or cancel them first.");
        }

        item.Name = item.Name.Trim();
        // SKU is identity for stock history and REQs — immutable after create.
        item.Sku = existing.Sku;
        item.QuantityOnHand = existing.QuantityOnHand;
        item.QuantityReserved = existing.QuantityReserved;

        _dbContext.Set<InventoryItem>().Update(item);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task RecordStockTransactionAsync(Guid itemId, decimal quantityChange, StockTransactionType type, string? reference = null, Guid? jobId = null, string? notes = null, CancellationToken ct = default)
    {
        var item = await _dbContext.Set<InventoryItem>().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item == null)
            throw new InvalidOperationException("Inventory item not found.");

        if (quantityChange == 0)
            throw new InvalidOperationException("Stock transaction quantity cannot be zero.");
        if (Math.Abs(quantityChange) > 1_000_000m)
            throw new InvalidOperationException("Stock transaction quantity magnitude cannot exceed 1,000,000.");

        if (!item.IsActive && type is StockTransactionType.Issue or StockTransactionType.Receipt)
            throw new InvalidOperationException(
                $"Cannot post {type} transactions for inactive item {item.Sku}.");

        // Block issues/adjustments that would drive stock negative (returns/receipts still allowed).
        if (quantityChange < 0 && item.QuantityOnHand + quantityChange < 0)
        {
            throw new InvalidOperationException(
                $"Insufficient stock for {item.Name}. On hand: {item.QuantityOnHand:N2}, change: {quantityChange:N2}.");
        }

        if (jobId is { } linkedJobId && linkedJobId != Guid.Empty)
        {
            var job = await _dbContext.Set<Job>().AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == linkedJobId, ct)
                ?? throw new InvalidOperationException("Linked job not found.");

            // Issues against a job require the job still open for field ops.
            if (type == StockTransactionType.Issue && !job.IsOpenForOperations())
                throw new InvalidOperationException(
                    $"Cannot issue stock to job {job.JobNumber} — job is {job.Status}.");
        }

        if (!string.IsNullOrWhiteSpace(reference) && reference.Trim().Length > 100)
            throw new InvalidOperationException("Stock transaction reference cannot exceed 100 characters.");
        if (!string.IsNullOrWhiteSpace(notes) && notes.Trim().Length > 500)
            throw new InvalidOperationException("Stock transaction notes cannot exceed 500 characters.");

        // Update on-hand
        item.QuantityOnHand += quantityChange;

        var transaction = new StockTransaction
        {
            InventoryItemId = itemId,
            Type = type,
            Quantity = quantityChange,
            UnitCostAtTime = item.UnitCost,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            JobId = jobId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
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