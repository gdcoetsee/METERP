using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly AppDbContext _dbContext;
    private readonly IInventoryService _inventoryService;

    public PurchaseOrderService(AppDbContext dbContext, IInventoryService inventoryService)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
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
        var query = _dbContext.Set<PurchaseOrder>()
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
            po.PoNumber = $"PO-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        RecalculateTotals(po);

        _dbContext.Set<PurchaseOrder>().Add(po);
        await _dbContext.SaveChangesAsync(ct);
        return po.Id;
    }

    public async Task UpdateAsync(PurchaseOrder po, CancellationToken ct = default)
    {
        RecalculateTotals(po);
        _dbContext.Set<PurchaseOrder>().Update(po);
        await _dbContext.SaveChangesAsync(ct);
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
    }

    public async Task UpdateStatusAsync(Guid poId, PurchaseOrderStatus newStatus, CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>().FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return;

        po.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
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
    }

    public async Task ReceiveAsync(Guid poId, CancellationToken ct = default)
    {
        var po = await _dbContext.Set<PurchaseOrder>()
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == poId, ct);
        if (po == null) return;

        foreach (var line in po.Lines.Where(l => !l.IsDeleted && l.InventoryItemId.HasValue))
        {
            await _inventoryService.RecordStockTransactionAsync(
                line.InventoryItemId!.Value,
                line.Quantity,
                StockTransactionType.Receipt,
                po.PoNumber,
                null,
                $"PO receipt for line: {line.Description}",
                ct);
        }

        po.Status = PurchaseOrderStatus.Received;
        await _dbContext.SaveChangesAsync(ct);
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
