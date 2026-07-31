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
        var openExists = await _dbContext.Set<StockTakeSession>()
            .AnyAsync(s => s.Status == StockTakeStatus.Open, ct);
        if (openExists)
            throw new InvalidOperationException(
                "An open stock take session already exists. Post or cancel it before starting another.");

        var items = await _inventoryService.GetAllItemsAsync(pageSize: 500, ct: ct);
        var activeItems = items.Where(i => i.IsActive).ToList();
        if (activeItems.Count == 0)
            throw new InvalidOperationException(
                "Cannot start a stock take with no active inventory items. Add stock master records first.");

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

        foreach (var item in activeItems)
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
            await _audit.LogAsync("START", "StockTake", session.SessionNumber, $"{activeItems.Count} items", ct);

        return session.Id;
    }

    public async Task<bool> RecordCountAsync(Guid sessionId, Guid inventoryItemId, decimal countedQuantity, CancellationToken ct = default)
    {
        if (countedQuantity < 0)
            throw new InvalidOperationException("Counted quantity cannot be negative.");
        if (countedQuantity > 1_000_000m)
            throw new InvalidOperationException("Counted quantity cannot exceed 1,000,000.");

        var line = await _dbContext.Set<StockTakeLine>()
            .FirstOrDefaultAsync(l => l.StockTakeSessionId == sessionId && l.InventoryItemId == inventoryItemId, ct);
        if (line == null) return false;

        var session = await _dbContext.Set<StockTakeSession>().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null || session.Status != StockTakeStatus.Open) return false;

        line.CountedQuantity = countedQuantity;
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StockTakeVarianceSummary?> GetVarianceSummaryAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _dbContext.Set<StockTakeSession>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return null;

        var lines = session.Lines.Where(l => !l.IsDeleted).ToList();
        var counted = lines.Where(l => l.CountedQuantity.HasValue).ToList();
        var uncounted = lines.Count - counted.Count;
        var withVariance = 0;
        decimal positive = 0m;
        decimal negative = 0m;

        foreach (var line in counted)
        {
            var variance = line.CountedQuantity!.Value - line.SystemQuantity;
            if (variance == 0) continue;
            withVariance++;
            if (variance > 0) positive += variance;
            else negative += variance;
        }

        return new StockTakeVarianceSummary(
            counted.Count,
            uncounted,
            withVariance,
            positive,
            negative);
    }

    public async Task<bool> PostSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
    {
        var session = await _dbContext.Set<StockTakeSession>()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null || session.Status != StockTakeStatus.Open) return false;

        var countedLines = session.Lines.Where(l => !l.IsDeleted && l.CountedQuantity.HasValue).ToList();
        if (countedLines.Count == 0)
            throw new InvalidOperationException(
                "Record at least one physical count before posting variances.");

        foreach (var line in countedLines)
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
            await _audit.LogAsync("POST", "StockTake", session.SessionNumber,
                $"{countedLines.Count} line(s) counted — variances posted", ct);

        return true;
    }

    public async Task<bool> CancelSessionAsync(Guid sessionId, Guid userId, string? reason = null, CancellationToken ct = default)
    {
        if (reason != null && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Cancellation reason cannot be blank when provided.", nameof(reason));

        var session = await _dbContext.Set<StockTakeSession>()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null || session.Status != StockTakeStatus.Open)
            return false;

        session.Status = StockTakeStatus.Cancelled;
        session.PostedAt = DateTime.UtcNow;
        session.PostedByUserId = userId;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            session.Notes = string.IsNullOrWhiteSpace(session.Notes)
                ? $"Cancelled: {reason.Trim()}"
                : $"{session.Notes.Trim()} | Cancelled: {reason.Trim()}";
        }

        await _dbContext.SaveChangesAsync(ct);

        if (_audit != null)
            await _audit.LogAsync("CANCEL", "StockTake", session.SessionNumber,
                reason ?? "Session cancelled without posting", ct);

        return true;
    }
}