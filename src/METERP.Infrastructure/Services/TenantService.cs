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

    public async Task<IReadOnlyList<Tenant>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var query = _dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Name.ToLower().Contains(term) ||
                t.Subdomain.ToLower().Contains(term));
        }

        return await query
            .OrderBy(t => t.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(string name, string subdomain, CancellationToken ct = default)
    {
        var tenant = new Tenant
        {
            Name = name.Trim(),
            Subdomain = subdomain.Trim().ToLowerInvariant(),
            IsActive = true,
            EnabledFeatures = "ai,usage-tracking" // default sellable features stub
        };

        // Note: TenantId will be set by DbContext SaveChanges, but for the very first tenant(s)
        // we may want to allow empty or use a special value. For foundation we let it be Guid.Empty
        // or the current one. In practice the first tenant creation often happens outside a tenant context.

        _dbContext.Tenants.Add(tenant);
        await _dbContext.SaveChangesAsync(ct);

        return tenant.Id;
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        var existing = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenant.Id, ct);

        if (existing == null) return;

        existing.Name = tenant.Name?.Trim() ?? string.Empty;
        existing.Subdomain = tenant.Subdomain?.Trim().ToLowerInvariant() ?? string.Empty;
        existing.IsActive = tenant.IsActive;

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

    public async Task IncrementJobCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant != null)
        {
            tenant.TotalJobsCreated++;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task IncrementAiCallCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant != null)
        {
            tenant.TotalAiCalls++;
            tenant.LastActivityUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task IncrementQuoteCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant != null)
        {
            tenant.TotalQuotesCreated++;
            tenant.LastActivityUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task IncrementInvoiceCountAsync(Guid tenantId, decimal revenueAmount = 0, CancellationToken ct = default)
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant != null)
        {
            tenant.TotalInvoicesIssued++;
            tenant.TotalRevenueBilled += revenueAmount;
            tenant.LastActivityUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }
    }
}
