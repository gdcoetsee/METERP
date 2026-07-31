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
        if (string.IsNullOrWhiteSpace(emp.FirstName) || string.IsNullOrWhiteSpace(emp.LastName))
            throw new InvalidOperationException("First and last name are required.");
        if (emp.DefaultHourlyRate < 0)
            throw new InvalidOperationException("Default hourly rate cannot be negative.");
        if (emp.DefaultHourlyRate > 50_000m)
            throw new InvalidOperationException("Default hourly rate cannot exceed 50,000.");
        if (emp.LeaveBalanceDays < 0)
            throw new InvalidOperationException("Leave balance cannot be negative.");
        if (emp.AnnualLeaveEntitlementDays < 0)
            throw new InvalidOperationException("Annual leave entitlement cannot be negative.");
        if (emp.AnnualLeaveEntitlementDays > 60m)
            throw new InvalidOperationException("Annual leave entitlement cannot exceed 60 days.");
        if (emp.MandatoryHoursPerMonth <= 0)
            emp.MandatoryHoursPerMonth = 160m;
        if (emp.MandatoryHoursPerMonth > 400m)
            throw new InvalidOperationException("Mandatory hours per month cannot exceed 400.");

        emp.FirstName = emp.FirstName.Trim();
        emp.LastName = emp.LastName.Trim();
        if (emp.FirstName.Length > 100 || emp.LastName.Length > 100)
            throw new InvalidOperationException("Employee first and last names cannot exceed 100 characters each.");
        if (!string.IsNullOrWhiteSpace(emp.JobTitle))
        {
            emp.JobTitle = emp.JobTitle.Trim();
            if (emp.JobTitle.Length > 100)
                throw new InvalidOperationException("Job title cannot exceed 100 characters.");
        }
        if (!string.IsNullOrWhiteSpace(emp.Notes))
        {
            emp.Notes = emp.Notes.Trim();
            if (emp.Notes.Length > 2000)
                throw new InvalidOperationException("Employee notes cannot exceed 2000 characters.");
        }
        if (!string.IsNullOrWhiteSpace(emp.Email))
        {
            emp.Email = emp.Email.Trim();
            if (!IsPlausibleEmail(emp.Email))
                throw new InvalidOperationException("Employee email must be a valid address.");
        }
        if (!string.IsNullOrWhiteSpace(emp.Phone))
            emp.Phone = emp.Phone.Trim();

        if (string.IsNullOrWhiteSpace(emp.EmployeeNumber))
        {
            emp.EmployeeNumber = $"EMP-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }
        else
        {
            emp.EmployeeNumber = emp.EmployeeNumber.Trim();
            var dup = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.EmployeeNumber == emp.EmployeeNumber, ct);
            if (dup)
                throw new InvalidOperationException($"Employee number '{emp.EmployeeNumber}' already exists.");
        }

        if (!string.IsNullOrWhiteSpace(emp.Email))
        {
            var emailDup = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Email == emp.Email, ct);
            if (emailDup)
                throw new InvalidOperationException($"Employee email '{emp.Email}' is already in use.");
        }

        if (emp.ManagerEmployeeId is { } managerId && managerId != Guid.Empty)
        {
            var managerOk = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Id == managerId && e.IsActive, ct);
            if (!managerOk)
                throw new InvalidOperationException("Manager employee not found or inactive.");
        }

        if (emp.DivisionId is { } divisionId && divisionId != Guid.Empty)
        {
            var divisionOk = await _dbContext.Set<Division>()
                .AnyAsync(d => d.Id == divisionId && d.IsActive, ct);
            if (!divisionOk)
                throw new InvalidOperationException("Division not found or inactive.");
        }

        if (emp.HireDate is { } hireDate && hireDate.Date > DateTime.UtcNow.Date.AddDays(30))
            throw new InvalidOperationException("Hire date cannot be more than 30 days in the future.");

        if (emp.LinkedUserId is { } linkedUser && linkedUser != Guid.Empty)
        {
            var linkedTaken = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.LinkedUserId == linkedUser, ct);
            if (linkedTaken)
                throw new InvalidOperationException("That user is already linked to another employee.");
        }

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
        if (emp.DefaultHourlyRate < 0)
            throw new InvalidOperationException("Default hourly rate cannot be negative.");
        if (emp.DefaultHourlyRate > 50_000m)
            throw new InvalidOperationException("Default hourly rate cannot exceed 50,000.");
        if (emp.LeaveBalanceDays < 0)
            throw new InvalidOperationException("Leave balance cannot be negative.");
        if (emp.AnnualLeaveEntitlementDays < 0)
            throw new InvalidOperationException("Annual leave entitlement cannot be negative.");
        if (emp.AnnualLeaveEntitlementDays > 60m)
            throw new InvalidOperationException("Annual leave entitlement cannot exceed 60 days.");
        if (emp.MandatoryHoursPerMonth > 0 && emp.MandatoryHoursPerMonth > 400m)
            throw new InvalidOperationException("Mandatory hours per month cannot exceed 400.");

        var number = emp.EmployeeNumber.Trim();
        var dup = await _dbContext.Set<Employee>()
            .AnyAsync(e => e.EmployeeNumber == number && e.Id != emp.Id, ct);
        if (dup)
            throw new InvalidOperationException($"Employee number '{number}' already exists.");

        if (!string.IsNullOrWhiteSpace(emp.Email))
        {
            emp.Email = emp.Email.Trim();
            if (!IsPlausibleEmail(emp.Email))
                throw new InvalidOperationException("Employee email must be a valid address.");
            var emailDup = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Email == emp.Email && e.Id != emp.Id, ct);
            if (emailDup)
                throw new InvalidOperationException($"Employee email '{emp.Email}' is already in use.");
        }
        if (!string.IsNullOrWhiteSpace(emp.Phone))
            emp.Phone = emp.Phone.Trim();

        if (!emp.IsActive && existing.IsActive)
        {
            var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
                .AnyAsync(j => j.AssignedEmployeeId == emp.Id
                    && j.Status != JobStatus.Cancelled
                    && j.Status != JobStatus.Closed, ct);
            if (hasOpenJobs)
                throw new InvalidOperationException(
                    "Cannot deactivate an employee assigned to open jobs. Reassign or close those jobs first.");
        }

        if (emp.ManagerEmployeeId is { } managerId && managerId != Guid.Empty)
        {
            if (managerId == emp.Id)
                throw new InvalidOperationException("An employee cannot be their own manager.");
            var managerOk = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.Id == managerId && e.IsActive, ct);
            if (!managerOk)
                throw new InvalidOperationException("Manager employee not found or inactive.");
        }

        if (emp.DivisionId is { } divisionId && divisionId != Guid.Empty)
        {
            var divisionOk = await _dbContext.Set<Division>()
                .AnyAsync(d => d.Id == divisionId && d.IsActive, ct);
            if (!divisionOk)
                throw new InvalidOperationException("Division not found or inactive.");
        }

        if (emp.HireDate is { } hireDate && hireDate.Date > DateTime.UtcNow.Date.AddDays(30))
            throw new InvalidOperationException("Hire date cannot be more than 30 days in the future.");

        if (emp.LinkedUserId is { } linkedUser && linkedUser != Guid.Empty
            && emp.LinkedUserId != existing.LinkedUserId)
        {
            var linkedTaken = await _dbContext.Set<Employee>()
                .AnyAsync(e => e.LinkedUserId == linkedUser && e.Id != emp.Id, ct);
            if (linkedTaken)
                throw new InvalidOperationException("That user is already linked to another employee.");
        }

        var first = emp.FirstName.Trim();
        var last = emp.LastName.Trim();
        if (first.Length > 100 || last.Length > 100)
            throw new InvalidOperationException("Employee first and last names cannot exceed 100 characters each.");

        string? jobTitle = null;
        if (!string.IsNullOrWhiteSpace(emp.JobTitle))
        {
            jobTitle = emp.JobTitle.Trim();
            if (jobTitle.Length > 100)
                throw new InvalidOperationException("Job title cannot exceed 100 characters.");
        }

        string? notes = null;
        if (!string.IsNullOrWhiteSpace(emp.Notes))
        {
            notes = emp.Notes.Trim();
            if (notes.Length > 2000)
                throw new InvalidOperationException("Employee notes cannot exceed 2000 characters.");
        }

        existing.EmployeeNumber = number;
        existing.FirstName = first;
        existing.LastName = last;
        existing.JobTitle = jobTitle;
        existing.DefaultHourlyRate = emp.DefaultHourlyRate;
        existing.IsActive = emp.IsActive;
        existing.Notes = notes;
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

        var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
            .AnyAsync(j => j.AssignedEmployeeId == id
                && j.Status != JobStatus.Cancelled
                && j.Status != JobStatus.Closed, ct);
        if (hasOpenJobs)
            throw new InvalidOperationException(
                "Cannot delete an employee assigned to open jobs. Reassign or close those jobs first.");

        var hasPendingLeave = await _dbContext.Set<LeaveRequest>().AsNoTracking()
            .AnyAsync(r => r.EmployeeId == id
                && r.Status != LeaveRequestStatus.Rejected
                && r.Status != LeaveRequestStatus.Cancelled
                && r.Status != LeaveRequestStatus.Approved, ct);
        if (hasPendingLeave)
            throw new InvalidOperationException(
                "Cannot delete an employee with pending leave requests. Resolve leave first.");

        // Soft-delete and deactivate so scheduling/payroll pickers hide the person.
        e.IsActive = false;
        e.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var e = await _dbContext.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Employee not found.");

        if (e.IsDeleted && isActive)
            throw new InvalidOperationException("Cannot reactivate a deleted employee. Restore first.");

        if (!isActive && e.IsActive)
        {
            var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
                .AnyAsync(j => j.AssignedEmployeeId == id
                    && j.Status != JobStatus.Cancelled
                    && j.Status != JobStatus.Closed, ct);
            if (hasOpenJobs)
                throw new InvalidOperationException(
                    "Cannot deactivate an employee assigned to open jobs. Reassign or close those jobs first.");
        }

        e.IsActive = isActive;
        await _dbContext.SaveChangesAsync(ct);
        InvalidateListCaches();
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnEmployeeMasterDataChanged(_cache);
    }

    private static bool IsPlausibleEmail(string email) =>
        email.Contains('@') && !email.StartsWith('@') && !email.EndsWith('@');
}
