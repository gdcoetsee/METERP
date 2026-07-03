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
        _dbContext.Set<Division>().Add(division);
        await _dbContext.SaveChangesAsync(ct);
        return division.Id;
    }

    public async Task UpdateAsync(Division division, CancellationToken ct = default)
    {
        _dbContext.Set<Division>().Update(division);
        await _dbContext.SaveChangesAsync(ct);
    }
}