using METERP.Application.Services;
using METERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace METERP.Infrastructure.Services;

public class TenantBillingViewService : ITenantBillingViewService
{
    private readonly AppDbContext _dbContext;

    public TenantBillingViewService(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task<TenantBillingView?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return null;

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant == null)
            return null;

        var status = tenant.SubscriptionStatus ?? string.Empty;
        var isPastDue = status is "past_due" or "unpaid";

        return new TenantBillingView(tenant, tenant.HasFeature("ai"), isPastDue);
    }
}