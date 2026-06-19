using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class SupplierService : ISupplierService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public SupplierService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Supplier>()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Supplier>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                "suppliers",
                $"p{page}:s{pageSize}",
                () => LoadSuppliersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadSuppliersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Supplier>> LoadSuppliersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Supplier>()
            .AsNoTracking()
            .Where(s => s.IsActive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(term)) ||
                (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        return await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Supplier supplier, CancellationToken ct = default)
    {
        _dbContext.Set<Supplier>().Add(supplier);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return supplier.Id;
    }

    public async Task UpdateAsync(Supplier supplier, CancellationToken ct = default)
    {
        _dbContext.Set<Supplier>().Update(supplier);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var supplier = await _dbContext.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier == null) return;

        supplier.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches() => _cache?.InvalidateCategory("suppliers");
}