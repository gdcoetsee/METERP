using METERP.Domain;

namespace METERP.Common;

/// <summary>
/// Default monthly quotas and feature flags per subscription tier.
/// Tenant-level overrides (Max*PerMonth) take precedence when set.
/// </summary>
public static class TenantQuotaDefaults
{
    public static (int? Quotes, int? Jobs, int? Invoices, int? AiCalls) GetMonthlyLimits(SubscriptionTier tier) =>
        tier switch
        {
            SubscriptionTier.Demo => (50, 25, 25, 50),
            SubscriptionTier.Starter => (20, 10, 10, 30),
            SubscriptionTier.Professional => (500, 200, 200, 1000),
            SubscriptionTier.Enterprise => (null, null, null, null),
            _ => (20, 10, 10, 30)
        };

    public static string GetDefaultFeatures(SubscriptionTier tier) =>
        tier switch
        {
            SubscriptionTier.Starter => "usage-tracking",
            SubscriptionTier.Professional => "ai,usage-tracking,advanced-reports",
            SubscriptionTier.Enterprise => "ai,usage-tracking,advanced-reports,compliance",
            SubscriptionTier.Demo => "ai,usage-tracking",
            _ => "usage-tracking"
        };

    public static void ApplyTierDefaults(Tenant tenant)
    {
        var (quotes, jobs, invoices, ai) = GetMonthlyLimits(tenant.Tier);

        tenant.MaxQuotesPerMonth ??= quotes;
        tenant.MaxJobsPerMonth ??= jobs;
        tenant.MaxInvoicesPerMonth ??= invoices;
        tenant.MaxAiCallsPerMonth ??= ai;

        if (string.IsNullOrWhiteSpace(tenant.EnabledFeatures))
            tenant.EnabledFeatures = GetDefaultFeatures(tenant.Tier);
    }

    public static int? GetEffectiveLimit(Tenant tenant, QuotaType type)
    {
        var (quotes, jobs, invoices, ai) = GetMonthlyLimits(tenant.Tier);

        return type switch
        {
            QuotaType.Quote => tenant.MaxQuotesPerMonth ?? quotes,
            QuotaType.Job => tenant.MaxJobsPerMonth ?? jobs,
            QuotaType.Invoice => tenant.MaxInvoicesPerMonth ?? invoices,
            QuotaType.AiCall => tenant.MaxAiCallsPerMonth ?? ai,
            _ => null
        };
    }
}