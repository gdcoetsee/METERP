using METERP.Domain;

namespace METERP.Application.Services;

public interface IStockTakeService
{
    Task<StockTakeSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<StockTakeSession>> GetAllAsync(CancellationToken ct = default);

    Task<Guid> StartSessionAsync(Guid userId, string? notes = null, CancellationToken ct = default);

    Task<bool> RecordCountAsync(Guid sessionId, Guid inventoryItemId, decimal countedQuantity, CancellationToken ct = default);

    Task<bool> PostSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default);
}