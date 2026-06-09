using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Service for managing customer and company assets (Transformers, etc.).
/// </summary>
public interface IAssetService
{
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Asset>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Asset asset, CancellationToken ct = default);
    Task UpdateAsync(Asset asset, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid assetId, AssetStatus newStatus, CancellationToken ct = default);

    /// <summary>
    /// Record simple maintenance or work note against the asset (lightweight history).
    /// </summary>
    Task AddMaintenanceNoteAsync(Guid assetId, string note, Guid? jobId = null, CancellationToken ct = default);
}
