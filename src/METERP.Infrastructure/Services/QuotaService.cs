using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class QuotaService : IQuotaService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public QuotaService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task EnsureAllowedAsync(Guid tenantId, QuotaType type, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        if (tenant == null) return;

        await EnsureCurrentPeriodAsync(tenant, db, ct);

        var limit = TenantQuotaDefaults.GetEffectiveLimit(tenant, type);
        if (limit == null) return;

        var used = tenant.GetPeriodUsage(type);
        if (used >= limit.Value)
            throw new QuotaExceededException(type, limit.Value, used);
    }

    internal static DateTime GetCurrentPeriodStartUtc() =>
        new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static async Task EnsureCurrentPeriodAsync(Tenant tenant, AppDbContext db, CancellationToken ct)
    {
        var periodStart = GetCurrentPeriodStartUtc();
        if (tenant.UsagePeriodStartUtc.HasValue && tenant.UsagePeriodStartUtc.Value >= periodStart)
            return;

        tenant.UsagePeriodStartUtc = periodStart;
        tenant.PeriodQuotesCreated = 0;
        tenant.PeriodJobsCreated = 0;
        tenant.PeriodInvoicesIssued = 0;
        tenant.PeriodAiCalls = 0;
        await db.SaveChangesAsync(ct);
    }

}