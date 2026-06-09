using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class AssetService : IAssetService
{
    private readonly AppDbContext _dbContext;

    public AssetService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Asset>()
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<IReadOnlyList<Asset>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _dbContext.Set<Asset>()
            .Include(a => a.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Name.ToLower().Contains(term) ||
                a.AssetNumber.ToLower().Contains(term) ||
                (a.SerialNumber != null && a.SerialNumber.ToLower().Contains(term)) ||
                (a.Location != null && a.Location.ToLower().Contains(term)) ||
                (a.Customer != null && a.Customer.Name.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Asset asset, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(asset.AssetNumber))
        {
            asset.AssetNumber = $"AST-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        _dbContext.Set<Asset>().Add(asset);
        await _dbContext.SaveChangesAsync(ct);
        return asset.Id;
    }

    public async Task UpdateAsync(Asset asset, CancellationToken ct = default)
    {
        _dbContext.Set<Asset>().Update(asset);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset == null) return;

        asset.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid assetId, AssetStatus newStatus, CancellationToken ct = default)
    {
        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset == null) return;

        asset.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AddMaintenanceNoteAsync(Guid assetId, string note, Guid? jobId = null, CancellationToken ct = default)
    {
        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset == null) return;

        // For MVP we append to Notes. In a fuller version we'd have AssetMaintenance entity.
        var prefix = jobId.HasValue ? $"[Job {jobId}] " : "";
        asset.Notes = string.IsNullOrWhiteSpace(asset.Notes)
            ? $"{prefix}{DateTime.UtcNow:yyyy-MM-dd}: {note}"
            : $"{asset.Notes}\n{prefix}{DateTime.UtcNow:yyyy-MM-dd}: {note}";

        await _dbContext.SaveChangesAsync(ct);
    }
}
