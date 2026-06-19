using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent demo quota setup for quota-exceeded E2E (Acme tenant only).
/// </summary>
public static class E2EDemoQuotaSeeder
{
    public const int DemoMonthlyLimit = 10_000;

    public static async Task EnsureQuoteQuotaExceededAsync(
        ITenantService tenantService,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var tenant = await tenantService.GetByIdAsync(tenantId, ct);
        if (tenant == null)
            return;

        tenant.MaxQuotesPerMonth = 1;
        tenant.PeriodQuotesCreated = 1;
        tenant.UsagePeriodStartUtc = QuotaService.GetCurrentPeriodStartUtc();
        await tenantService.UpdateAsync(tenant, ct);
    }

    public static async Task ResetDemoQuotasAsync(
        ITenantService tenantService,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var tenant = await tenantService.GetByIdAsync(tenantId, ct);
        if (tenant == null)
            return;

        tenant.MaxQuotesPerMonth = DemoMonthlyLimit;
        tenant.MaxJobsPerMonth = DemoMonthlyLimit;
        tenant.MaxInvoicesPerMonth = DemoMonthlyLimit;
        tenant.MaxAiCallsPerMonth = DemoMonthlyLimit;
        tenant.PeriodQuotesCreated = 0;
        tenant.PeriodJobsCreated = 0;
        tenant.PeriodInvoicesIssued = 0;
        tenant.PeriodAiCalls = 0;
        tenant.UsagePeriodStartUtc = QuotaService.GetCurrentPeriodStartUtc();
        await tenantService.UpdateAsync(tenant, ct);
    }
}