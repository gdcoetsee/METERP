using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public EmployeeService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<Employee?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Employee>().FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Employees,
                $"p{page}:s{pageSize}",
                () => LoadEmployeesAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadEmployeesAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<Employee>> LoadEmployeesAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Set<Employee>().AsNoTracking().Where(e => e.IsActive).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e => e.FirstName.ToLower().Contains(term) || e.LastName.ToLower().Contains(term) || e.EmployeeNumber.ToLower().Contains(term));
        }

        query = query.OrderBy(e => e.LastName);

        if (page > 0 && pageSize > 0)
        {
            query = query.Skip((page - 1) * pageSize).Take(pageSize);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Employee emp, CancellationToken ct = default)
    {
        _dbContext.Set<Employee>().Add(emp);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return emp.Id;
    }

    public async Task UpdateAsync(Employee emp, CancellationToken ct = default)
    {
        _dbContext.Set<Employee>().Update(emp);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _dbContext.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e == null) return;
        e.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnEmployeeMasterDataChanged(_cache);
    }
}
