using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class DivisionService : IDivisionService
{
    private readonly AppDbContext _dbContext;

    public DivisionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Division>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        var query = _dbContext.Set<Division>().AsNoTracking().AsQueryable();
        if (activeOnly)
            query = query.Where(d => d.IsActive);

        return await query.OrderBy(d => d.Name).ToListAsync(ct);
    }

    public async Task<Division?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Set<Division>().FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<Guid> CreateAsync(Division division, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(division.Name))
            throw new InvalidOperationException("Division name is required.");

        division.Name = division.Name.Trim();
        if (string.IsNullOrWhiteSpace(division.Code))
            division.Code = division.Name.Length <= 6
                ? division.Name.ToUpperInvariant()
                : division.Name[..6].ToUpperInvariant();
        else
            division.Code = division.Code.Trim().ToUpperInvariant();

        var duplicate = await _dbContext.Set<Division>()
            .AnyAsync(d => d.Code == division.Code, ct);
        if (duplicate)
            throw new InvalidOperationException($"Division code '{division.Code}' already exists.");

        _dbContext.Set<Division>().Add(division);
        await _dbContext.SaveChangesAsync(ct);
        return division.Id;
    }

    public async Task UpdateAsync(Division division, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(division.Name))
            throw new InvalidOperationException("Division name is required.");

        division.Name = division.Name.Trim();
        if (string.IsNullOrWhiteSpace(division.Code))
            throw new InvalidOperationException("Division code is required.");

        division.Code = division.Code.Trim().ToUpperInvariant();

        var duplicate = await _dbContext.Set<Division>()
            .AnyAsync(d => d.Code == division.Code && d.Id != division.Id, ct);
        if (duplicate)
            throw new InvalidOperationException($"Division code '{division.Code}' already exists.");

        _dbContext.Set<Division>().Update(division);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var division = await _dbContext.Set<Division>().FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("Division not found.");

        if (!isActive && division.IsActive)
        {
            var hasOpenJobs = await _dbContext.Set<Job>().AsNoTracking()
                .AnyAsync(j => j.DivisionId == id
                    && j.Status != JobStatus.Cancelled
                    && j.Status != JobStatus.Closed, ct);
            if (hasOpenJobs)
                throw new InvalidOperationException(
                    "Cannot deactivate a division with open jobs. Reassign or close those jobs first.");

            var hasActiveEmployees = await _dbContext.Set<Employee>().AsNoTracking()
                .AnyAsync(e => e.DivisionId == id && e.IsActive, ct);
            if (hasActiveEmployees)
                throw new InvalidOperationException(
                    "Cannot deactivate a division with active employees. Reassign them first.");
        }

        division.IsActive = isActive;
        await _dbContext.SaveChangesAsync(ct);
    }
}