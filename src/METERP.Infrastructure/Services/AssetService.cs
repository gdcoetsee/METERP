using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class AssetService : IAssetService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public AssetService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Asset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Asset>()
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<IReadOnlyList<Asset>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Assets,
                $"p{page}:s{pageSize}",
                () => LoadAssetsAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadAssetsAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Asset>> LoadAssetsAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Asset>()
            .AsNoTracking()
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
        if (string.IsNullOrWhiteSpace(asset.Name))
            throw new InvalidOperationException("Asset name is required.");
        if (asset.CustomerId == Guid.Empty)
            throw new InvalidOperationException("Customer is required for an asset.");

        var customer = await _dbContext.Set<Customer>().FindAsync([asset.CustomerId], ct);
        if (customer == null || customer.IsDeleted)
            throw new InvalidOperationException("Customer not found.");

        asset.Name = asset.Name.Trim();
        if (!string.IsNullOrWhiteSpace(asset.AssetType))
            asset.AssetType = asset.AssetType.Trim();

        if (string.IsNullOrWhiteSpace(asset.AssetNumber))
        {
            asset.AssetNumber = $"AST-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        _dbContext.Set<Asset>().Add(asset);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return asset.Id;
    }

    public async Task UpdateAsync(Asset asset, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(asset.Name))
            throw new InvalidOperationException("Asset name is required.");

        var existing = await _dbContext.Set<Asset>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == asset.Id, ct)
            ?? throw new InvalidOperationException("Asset not found.");

        asset.Name = asset.Name.Trim();
        asset.AssetNumber = existing.AssetNumber;
        if (asset.CustomerId == Guid.Empty)
            asset.CustomerId = existing.CustomerId;
        else if (asset.CustomerId != existing.CustomerId)
        {
            var customer = await _dbContext.Set<Customer>().FindAsync([asset.CustomerId], ct);
            if (customer == null || customer.IsDeleted)
                throw new InvalidOperationException("Customer not found.");
        }

        _dbContext.Set<Asset>().Update(asset);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset == null) return;

        var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
            .AnyAsync(j => j.AssetId == id
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Closed, ct);
        if (hasOpenJobs)
            throw new InvalidOperationException(
                "Cannot delete an asset assigned to open jobs. Unassign or close those jobs first.");

        if (asset.Status == AssetStatus.Operational)
            asset.Status = AssetStatus.Decommissioned;

        asset.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task UpdateStatusAsync(Guid assetId, AssetStatus newStatus, CancellationToken ct = default)
    {
        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new InvalidOperationException("Asset not found.");

        if (asset.Status == AssetStatus.Decommissioned && newStatus != AssetStatus.Decommissioned)
            throw new InvalidOperationException(
                "Decommissioned assets cannot be returned to service without an explicit re-create / admin process.");

        asset.Status = newStatus;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task AddMaintenanceNoteAsync(Guid assetId, string note, Guid? jobId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Maintenance note is required.", nameof(note));

        var asset = await _dbContext.Set<Asset>().FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset == null)
            throw new InvalidOperationException("Asset not found.");

        string? jobNumber = null;
        if (jobId is { } linkedJobId && linkedJobId != Guid.Empty)
        {
            var job = await _dbContext.Set<Job>().AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == linkedJobId, ct)
                ?? throw new InvalidOperationException("Linked job not found.");
            jobNumber = job.JobNumber;
        }

        var prefix = jobNumber != null ? $"[Job {jobNumber}] " : "";
        asset.Notes = string.IsNullOrWhiteSpace(asset.Notes)
            ? $"{prefix}{DateTime.UtcNow:yyyy-MM-dd}: {note.Trim()}"
            : $"{asset.Notes}\n{prefix}{DateTime.UtcNow:yyyy-MM-dd}: {note.Trim()}";

        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnAssetMasterDataChanged(_cache);
    }
}
