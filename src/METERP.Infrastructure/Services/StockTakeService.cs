using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class StockTakeService : IStockTakeService
{
    private readonly AppDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly IDocumentSequenceService? _documentSequence;
    private readonly IAuditService? _audit;

    public StockTakeService(
        AppDbContext dbContext,
        IInventoryService inventoryService,
        IDocumentSequenceService? documentSequence = null,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _documentSequence = documentSequence;
        _audit = audit;
    }

    public async Task<StockTakeSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<StockTakeSession>()
            .Include(s => s.Lines).ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<StockTakeSession>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<StockTakeSession>()
            .AsNoTracking()
            .OrderByDescending(s => s.StartedAt)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<Guid> StartSessionAsync(Guid userId, string? notes = null, CancellationToken ct = default)
    {
        var items = await _inventoryService.GetAllItemsAsync(pageSize: 500, ct: ct);
        var session = new StockTakeSession
        {
            SessionNumber = _documentSequence != null
                ? await _documentSequence.GetNextNumberAsync("StockTake", "STK", ct)
                : $"STK-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}",
            StartedByUserId = userId,
            Notes = notes,
            Status = StockTakeStatus.Open
        };

        _dbContext.Set<StockTakeSession>().Add(session);
        await _dbContext.SaveChangesAsync(ct);

        foreach (var item in items.Where(i => i.IsActive))
        {
            _dbContext.Set<StockTakeLine>().Add(new StockTakeLine
            {
                StockTakeSessionId = session.Id,
                InventoryItemId = item.Id,
                SystemQuantity = item.QuantityOnHand
            });
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("START", "StockTake", session.SessionNumber, $"{items.Count} items", ct);

        return session.Id;
    }

    public async Task<bool> RecordCountAsync(Guid sessionId, Guid inventoryItemId, decimal countedQuantity, CancellationToken ct = default)
    {
        var line = await _dbContext.Set<StockTakeLine>()
            .FirstOrDefaultAsync(l => l.StockTakeSessionId == sessionId && l.InventoryItemId == inventoryItemId, ct);
        if (line == null) return false;

        var session = await _dbContext.Set<StockTakeSession>().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null || session.Status != StockTakeStatus.Open) return false;

        line.CountedQuantity = countedQuantity;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> PostSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await _dbContext.Set<StockTakeSession>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null || session.Status != StockTakeStatus.Open) return false;

        foreach (var line in session.Lines.Where(l => !l.IsDeleted && l.CountedQuantity.HasValue))
        {
            var variance = line.CountedQuantity!.Value - line.SystemQuantity;
            if (variance == 0) continue;

            await _inventoryService.RecordStockTransactionAsync(
                line.InventoryItemId,
                variance,
                StockTransactionType.Adjustment,
                session.SessionNumber,
                null,
                $"Stock take variance {variance:N2}",
                ct);
        }

        session.Status = StockTakeStatus.Posted;
        session.PostedAt = DateTime.UtcNow;
        session.PostedByUserId = userId;
        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("POST", "StockTake", session.SessionNumber, "Variances posted", ct);

        return true;
    }
}