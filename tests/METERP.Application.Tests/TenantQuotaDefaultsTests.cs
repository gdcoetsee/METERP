using METERP.Common;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class TenantQuotaDefaultsTests
{
    [Theory]
    [InlineData(SubscriptionTier.Starter, 20, 10, 10, 30)]
    [InlineData(SubscriptionTier.Professional, 500, 200, 200, 1000)]
    [InlineData(SubscriptionTier.Demo, 50, 25, 25, 50)]
    public void GetMonthlyLimits_ReturnsExpectedTierDefaults(
        SubscriptionTier tier, int quotes, int jobs, int invoices, int ai)
    {
        var (q, j, i, a) = TenantQuotaDefaults.GetMonthlyLimits(tier);

        Assert.Equal(quotes, q);
        Assert.Equal(jobs, j);
        Assert.Equal(invoices, i);
        Assert.Equal(ai, a);
    }

    [Fact]
    public void GetMonthlyLimits_Enterprise_IsUnlimited()
    {
        var (quotes, jobs, invoices, ai) = TenantQuotaDefaults.GetMonthlyLimits(SubscriptionTier.Enterprise);

        Assert.Null(quotes);
        Assert.Null(jobs);
        Assert.Null(invoices);
        Assert.Null(ai);
    }

    [Fact]
    public void ApplyBillingTier_ForceResetsLimitsAndFeatures()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 999,
            EnabledFeatures = "custom-only"
        };

        TenantQuotaDefaults.ApplyBillingTier(tenant, SubscriptionTier.Professional);

        Assert.Equal(SubscriptionTier.Professional, tenant.Tier);
        Assert.Equal(500, tenant.MaxQuotesPerMonth);
        Assert.Contains("ai", tenant.EnabledFeatures);
        Assert.DoesNotContain("custom-only", tenant.EnabledFeatures);
    }

    [Fact]
    public void ApplyTierDefaults_SetsLimitsAndFeatures_WhenUnset()
    {
        var tenant = new Tenant { Tier = SubscriptionTier.Professional };

        TenantQuotaDefaults.ApplyTierDefaults(tenant);

        Assert.Equal(500, tenant.MaxQuotesPerMonth);
        Assert.Equal(200, tenant.MaxJobsPerMonth);
        Assert.Contains("ai", tenant.EnabledFeatures);
    }

    [Fact]
    public void GetEffectiveLimit_RespectsTenantOverride()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 5
        };

        var limit = TenantQuotaDefaults.GetEffectiveLimit(tenant, QuotaType.Quote);

        Assert.Equal(5, limit);
    }

    [Fact]
    public void GetEffectiveLimit_FallsBackToTierDefault()
    {
        var tenant = new Tenant { Tier = SubscriptionTier.Starter };

        var limit = TenantQuotaDefaults.GetEffectiveLimit(tenant, QuotaType.Job);

        Assert.Equal(10, limit);
    }

    [Fact]
    public void IsAtOrOverLimit_ReturnsTrue_WhenUsageMeetsLimit()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 3,
            PeriodQuotesCreated = 3
        };

        Assert.True(TenantQuotaDefaults.IsAtOrOverLimit(tenant, QuotaType.Quote));
        Assert.False(TenantQuotaDefaults.IsAtOrOverLimit(tenant, QuotaType.Job));
    }

    [Fact]
    public void HasAnyQuotaAtOrOverLimit_ReturnsTrue_WhenAnyCounterAtCap()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Professional,
            PeriodJobsCreated = 200,
            MaxJobsPerMonth = 200
        };

        Assert.True(TenantQuotaDefaults.HasAnyQuotaAtOrOverLimit(tenant));
    }

    [Fact]
    public void GetQuotaStatus_ReturnsWarning_WhenUsageAtEightyPercent()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 10,
            PeriodQuotesCreated = 8
        };

        Assert.Equal(QuotaUsageStatus.Warning, TenantQuotaDefaults.GetQuotaStatus(tenant, QuotaType.Quote));
    }

    [Fact]
    public void GetQuotaTooltip_Exceeded_MentionsUpgrade()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 5,
            PeriodQuotesCreated = 5
        };

        var tooltip = TenantQuotaDefaults.GetQuotaTooltip(tenant, QuotaType.Quote, "Quotes");

        Assert.Contains("Limit reached", tooltip);
        Assert.Contains("upgrade", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetExceededQuotaLabels_ListsOnlyExceededCounters()
    {
        var tenant = new Tenant
        {
            Tier = SubscriptionTier.Starter,
            MaxQuotesPerMonth = 1,
            PeriodQuotesCreated = 1,
            MaxJobsPerMonth = 10,
            PeriodJobsCreated = 3
        };

        var labels = TenantQuotaDefaults.GetExceededQuotaLabels(tenant);

        Assert.Single(labels);
        Assert.Equal("Quotes", labels[0]);
    }
}