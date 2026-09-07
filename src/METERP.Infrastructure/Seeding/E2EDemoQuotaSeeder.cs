using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent demo quota setup for quota-exceeded E2E (Acme tenant only).
/// Uses ExecuteUpdate so leftover Blazor circuits cannot 500 on RowVersion conflicts.
/// </summary>
public static class E2EDemoQuotaSeeder
{
    public const int DemoMonthlyLimit = 10_000;

    public static Task EnsureQuoteQuotaExceededAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct = default) =>
        SetQuotaAtLimitAsync(db, tenantId, QuotaType.Quote, ct);

    public static Task EnsureJobQuotaExceededAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct = default) =>
        SetQuotaAtLimitAsync(db, tenantId, QuotaType.Job, ct);

    public static Task EnsureInvoiceQuotaExceededAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct = default) =>
        SetQuotaAtLimitAsync(db, tenantId, QuotaType.Invoice, ct);

    public static Task EnsureAiQuotaExceededAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct = default) =>
        SetQuotaAtLimitAsync(db, tenantId, QuotaType.AiCall, ct);

    private static async Task SetQuotaAtLimitAsync(
        AppDbContext db,
        Guid tenantId,
        QuotaType type,
        CancellationToken ct)
    {
        var periodStart = QuotaService.GetCurrentPeriodStartUtc();
        var updated = type switch
        {
            QuotaType.Quote => await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.MaxQuotesPerMonth, 1)
                    .SetProperty(t => t.PeriodQuotesCreated, 1)
                    .SetProperty(t => t.UsagePeriodStartUtc, periodStart), ct),
            QuotaType.Job => await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.MaxJobsPerMonth, 1)
                    .SetProperty(t => t.PeriodJobsCreated, 1)
                    .SetProperty(t => t.UsagePeriodStartUtc, periodStart), ct),
            QuotaType.Invoice => await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.MaxInvoicesPerMonth, 1)
                    .SetProperty(t => t.PeriodInvoicesIssued, 1)
                    .SetProperty(t => t.UsagePeriodStartUtc, periodStart), ct),
            QuotaType.AiCall => await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.MaxAiCallsPerMonth, 1)
                    .SetProperty(t => t.PeriodAiCalls, 1)
                    .SetProperty(t => t.UsagePeriodStartUtc, periodStart), ct),
            _ => 0
        };

        if (updated == 0)
            throw new InvalidOperationException($"Demo tenant {tenantId} was not updated for {type} quota.");
    }

    public static async Task ResetDemoQuotasAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var periodStart = QuotaService.GetCurrentPeriodStartUtc();
        var updated = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.MaxQuotesPerMonth, DemoMonthlyLimit)
                .SetProperty(t => t.MaxJobsPerMonth, DemoMonthlyLimit)
                .SetProperty(t => t.MaxInvoicesPerMonth, DemoMonthlyLimit)
                .SetProperty(t => t.MaxAiCallsPerMonth, DemoMonthlyLimit)
                .SetProperty(t => t.PeriodQuotesCreated, 0)
                .SetProperty(t => t.PeriodJobsCreated, 0)
                .SetProperty(t => t.PeriodInvoicesIssued, 0)
                .SetProperty(t => t.PeriodAiCalls, 0)
                .SetProperty(t => t.UsagePeriodStartUtc, periodStart), ct);

        if (updated == 0)
            throw new InvalidOperationException($"Demo tenant {tenantId} was not reset.");
    }
}
