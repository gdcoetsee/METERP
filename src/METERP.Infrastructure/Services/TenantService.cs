using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Basic implementation of ITenantService using EF Core.
/// In a full system this would include validation, subdomain uniqueness checks,
/// provisioning logic, etc.
/// </summary>
public class TenantService : ITenantService
{
    private readonly AppDbContext _dbContext;

    public TenantService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        // Use IgnoreQueryFilters because tenant listing/management may be cross-tenant or admin
        return await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
    }

    public async Task<Tenant?> GetBySubdomainAsync(string subdomain, CancellationToken ct = default)
    {
        return await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain && !t.IsDeleted, ct);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default)
    {
        var tenants = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return tenants;
    }

    public async Task<Guid> CreateAsync(string name, string subdomain, CancellationToken ct = default)
    {
        var tenant = new Tenant
        {
            Name = name.Trim(),
            Subdomain = subdomain.Trim().ToLowerInvariant(),
            IsActive = true
        };

        // Note: TenantId will be set by DbContext SaveChanges, but for the very first tenant(s)
        // we may want to allow empty or use a special value. For foundation we let it be Guid.Empty
        // or the current one. In practice the first tenant creation often happens outside a tenant context.

        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(ct);

        return tenant.Id;
    }

    public async Task UpdateAsync(Guid id, string name, bool isActive, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant == null) return;

        tenant.Name = name.Trim();
        tenant.IsActive = isActive;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tenant == null) return;

        tenant.IsDeleted = true;
        await _dbContext.SaveChangesAsync(ct);
    }
}
