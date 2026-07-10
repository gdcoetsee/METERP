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
        return await _dbContext.Set<Employee>()
            .Include(e => e.Division)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<IReadOnlyList<Employee>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search) && !includeInactive)
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Employees,
                $"p{page}:s{pageSize}",
                () => LoadEmployeesAsync(search, page, pageSize, includeInactive, ct),
                ct: ct);
        }

        return await LoadEmployeesAsync(search, page, pageSize, includeInactive, ct);
    }

    private async Task<IReadOnlyList<Employee>> LoadEmployeesAsync(
        string? search,
        int page,
        int pageSize,
        bool includeInactive,
        CancellationToken ct)
    {
        var query = _dbContext.Set<Employee>()
            .AsNoTracking()
            .Include(e => e.Division)
            .AsQueryable();

        if (!includeInactive)
            query = query.Where(e => e.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(term) ||
                e.LastName.ToLower().Contains(term) ||
                e.EmployeeNumber.ToLower().Contains(term) ||
                (e.JobTitle != null && e.JobTitle.ToLower().Contains(term)) ||
                (e.Email != null && e.Email.ToLower().Contains(term)));
        }

        query = query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName);

        if (page > 0 && pageSize > 0)
            query = query.Skip((page - 1) * pageSize).Take(pageSize);

        return await query.ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(Employee emp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emp.EmployeeNumber))
            emp.EmployeeNumber = $"EMP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        if (string.IsNullOrWhiteSpace(emp.FirstName) || string.IsNullOrWhiteSpace(emp.LastName))
            throw new InvalidOperationException("First and last name are required.");
        if (emp.MandatoryHoursPerMonth <= 0)
            emp.MandatoryHoursPerMonth = 160m;

        _dbContext.Set<Employee>().Add(emp);
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
        return emp.Id;
    }

    public async Task UpdateAsync(Employee emp, CancellationToken ct = default)
    {
        var existing = await _dbContext.Set<Employee>().FirstOrDefaultAsync(e => e.Id == emp.Id, ct);
        if (existing == null)
            throw new InvalidOperationException("Employee not found.");

        if (string.IsNullOrWhiteSpace(emp.EmployeeNumber))
            throw new InvalidOperationException("Employee number is required.");
        if (string.IsNullOrWhiteSpace(emp.FirstName) || string.IsNullOrWhiteSpace(emp.LastName))
            throw new InvalidOperationException("First and last name are required.");

        existing.EmployeeNumber = emp.EmployeeNumber.Trim();
        existing.FirstName = emp.FirstName.Trim();
        existing.LastName = emp.LastName.Trim();
        existing.JobTitle = emp.JobTitle;
        existing.DefaultHourlyRate = emp.DefaultHourlyRate;
        existing.IsActive = emp.IsActive;
        existing.Notes = emp.Notes;
        existing.DivisionId = emp.DivisionId;
        existing.LinkedUserId = emp.LinkedUserId;
        existing.ManagerEmployeeId = emp.ManagerEmployeeId;
        existing.Email = emp.Email;
        existing.Phone = emp.Phone;
        existing.HireDate = emp.HireDate;
        existing.AnnualLeaveEntitlementDays = emp.AnnualLeaveEntitlementDays;
        existing.LeaveBalanceDays = emp.LeaveBalanceDays;
        existing.MandatoryHoursPerMonth = emp.MandatoryHoursPerMonth > 0 ? emp.MandatoryHoursPerMonth : 160m;

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
