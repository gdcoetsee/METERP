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

    /// <summary>
    /// Force-reset quotas and features when billing webhooks change subscription tier.
    /// </summary>
    public static void ApplyBillingTier(Tenant tenant, SubscriptionTier tier)
    {
        tenant.Tier = tier;
        var (quotes, jobs, invoices, ai) = GetMonthlyLimits(tier);
        tenant.MaxQuotesPerMonth = quotes;
        tenant.MaxJobsPerMonth = jobs;
        tenant.MaxInvoicesPerMonth = invoices;
        tenant.MaxAiCallsPerMonth = ai;
        tenant.EnabledFeatures = GetDefaultFeatures(tier);
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

    public static int GetPeriodUsage(Tenant tenant, QuotaType type) =>
        type switch
        {
            QuotaType.Quote => tenant.PeriodQuotesCreated,
            QuotaType.Job => tenant.PeriodJobsCreated,
            QuotaType.Invoice => tenant.PeriodInvoicesIssued,
            QuotaType.AiCall => tenant.PeriodAiCalls,
            _ => 0
        };

    public static bool IsAtOrOverLimit(Tenant tenant, QuotaType type)
    {
        var limit = GetEffectiveLimit(tenant, type);
        return limit.HasValue && GetPeriodUsage(tenant, type) >= limit.Value;
    }

    public static bool HasAnyQuotaAtOrOverLimit(Tenant tenant) =>
        IsAtOrOverLimit(tenant, QuotaType.Quote)
        || IsAtOrOverLimit(tenant, QuotaType.Job)
        || IsAtOrOverLimit(tenant, QuotaType.Invoice)
        || IsAtOrOverLimit(tenant, QuotaType.AiCall);

    public static QuotaUsageStatus GetQuotaStatus(Tenant tenant, QuotaType type)
    {
        var limit = GetEffectiveLimit(tenant, type);
        if (!limit.HasValue)
            return QuotaUsageStatus.Unlimited;

        var used = GetPeriodUsage(tenant, type);
        if (used >= limit.Value)
            return QuotaUsageStatus.Exceeded;

        if (used >= limit.Value * 0.8m)
            return QuotaUsageStatus.Warning;

        return QuotaUsageStatus.Ok;
    }

    public static string GetQuotaTooltip(Tenant tenant, QuotaType type, string label)
    {
        var limit = GetEffectiveLimit(tenant, type);
        var used = GetPeriodUsage(tenant, type);

        if (!limit.HasValue)
            return $"{label}: {used} used this month (unlimited on your plan).";

        return GetQuotaStatus(tenant, type) switch
        {
            QuotaUsageStatus.Exceeded =>
                $"{label}: {used} of {limit} monthly limit used. Limit reached — upgrade your plan or wait for the period to reset.",
            QuotaUsageStatus.Warning =>
                $"{label}: {used} of {limit} monthly limit used. Approaching limit ({Math.Round(used * 100m / limit.Value)}%).",
            _ => $"{label}: {used} of {limit} monthly limit used this month."
        };
    }

    public static IReadOnlyList<string> GetExceededQuotaLabels(Tenant tenant) =>
        QuotaDisplayNames
            .Where(pair => IsAtOrOverLimit(tenant, pair.Type))
            .Select(pair => pair.Label)
            .ToList();

    private static readonly (string Label, QuotaType Type)[] QuotaDisplayNames =
    [
        ("Quotes", QuotaType.Quote),
        ("Jobs", QuotaType.Job),
        ("Invoices", QuotaType.Invoice),
        ("AI calls", QuotaType.AiCall)
    ];
}

public enum QuotaUsageStatus
{
    Unlimited,
    Ok,
    Warning,
    Exceeded
}