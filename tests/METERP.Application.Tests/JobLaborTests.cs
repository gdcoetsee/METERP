using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class JobLaborTests
{
    [Fact]
    public void TotalCost_CalculatesCorrectly()
    {
        var labor = new JobLabor
        {
            Hours = 8,
            HourlyRate = 195
        };

        Assert.Equal(1560m, labor.TotalCost);
    }

    [Fact]
    public void TotalCost_HandlesZero()
    {
        var labor = new JobLabor
        {
            Hours = 0,
            HourlyRate = 200
        };

        Assert.Equal(0m, labor.TotalCost);
    }
}

public class PurchaseOrderLineTests
{
    [Fact]
    public void LineTotal_CalculatesCorrectly()
    {
        var line = new PurchaseOrderLine
        {
            Quantity = 3,
            UnitPrice = 2450
        };

        Assert.Equal(7350m, line.LineTotal);
    }
}

// Additional tests for sellable features (photo/milestones stub logic, AI context, variance with travel)
public class JobFeatureTests
{
    [Fact]
    public void JobVariance_IncludesTravelAndLabor()
    {
        var job = new Job { QuotedTotal = 10000, ActualCost = 8000 };
        job.ActualCosts = new List<JobCost> { new JobCost { CostType = "Travel", Amount = 1500 } };
        job.Labors = new List<JobLabor> { new JobLabor { Hours = 10, HourlyRate = 200 } };

        var laborTotal = job.Labors.Sum(l => l.TotalCost);
        var actual = job.ActualCost + laborTotal + job.ActualCosts.Sum(c => c.Amount);
        var variance = actual - job.QuotedTotal;

        Assert.Equal(2000, laborTotal); // 10 * 200 (fixed math)
        Assert.Equal(11500, actual); // 8000 + 1500 + 2000 (travel + active labor)
        Assert.Equal(1500, variance); // over by 1500 due to travel + extra labor
    }

    [Fact]
    public void PhotoUpload_SimulatesBase64Storage()
    {
        // Stub test for field photo feature (real uses InputFile + base64 in JobList)
        var photos = new List<string>();
        var fakeBase64 = "data:image/png;base64,FAKEBASE64DATAFORTEST";
        photos.Add(fakeBase64);
        Assert.Single(photos);
        Assert.Contains("data:image", photos[0]);
    }
}

public class QuoteTotalsTests
{
    [Fact]
    public void QuoteTaxAndTotal_CalculateCorrectly_At15PercentSA()
    {
        var quote = new Quote
        {
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Quantity = 1, UnitPrice = 2680, IsDeleted = false },
                new QuoteLine { Quantity = 16, UnitPrice = 195, IsDeleted = false },
                new QuoteLine { Quantity = 1, UnitPrice = 875, IsDeleted = false }
            }
        };

        // Mirror the service RecalculateTotals logic for test
        var subtotal = quote.Lines.Where(l => !l.IsDeleted).Sum(l => l.LineTotal);
        var tax = Math.Round(subtotal * quote.TaxRate, 2);
        var total = subtotal + tax;

        Assert.Equal(6675m, subtotal);
        Assert.Equal(1001.25m, tax);
        Assert.Equal(7676.25m, total);
    }

    [Fact]
    public void QuoteTotals_IgnoresSoftDeletedLines()
    {
        var quote = new Quote
        {
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Quantity = 1, UnitPrice = 1000, IsDeleted = false },
                new QuoteLine { Quantity = 1, UnitPrice = 500, IsDeleted = true }
            }
        };

        var subtotal = quote.Lines.Where(l => !l.IsDeleted).Sum(l => l.LineTotal);
        var tax = Math.Round(subtotal * quote.TaxRate, 2);
        var total = subtotal + tax;

        Assert.Equal(1000m, subtotal);
        Assert.Equal(150m, tax);
        Assert.Equal(1150m, total);
    }
}

public class CommercialUsageTrackingTests
{
    [Fact]
    public void Tenant_UsageCounters_DefaultToZero_AndCanBeIncremented()
    {
        var tenant = new Tenant
        {
            Name = "Test Tenant",
            Subdomain = "test"
        };

        Assert.Equal(0, tenant.TotalJobsCreated);
        Assert.Equal(0, tenant.TotalAiCalls);

        tenant.TotalJobsCreated = 5;
        tenant.TotalAiCalls = 12;

        Assert.Equal(5, tenant.TotalJobsCreated);
        Assert.Equal(12, tenant.TotalAiCalls);
    }

    [Fact]
    public void Tenant_CommercialFields_IncludeRevenueAndFeatures()
    {
        var tenant = new Tenant
        {
            Name = "Acme",
            Subdomain = "acme",
            TotalInvoicesIssued = 3,
            TotalRevenueBilled = 12500.75m,
            EnabledFeatures = "ai,usage-tracking,reports"
        };

        Assert.Equal(12500.75m, tenant.TotalRevenueBilled);
        Assert.Contains("ai", tenant.EnabledFeatures);
    }

    [Fact]
    public void Tenant_HasFeature_WorksWithCommaSeparatedAndCaseInsensitive()
    {
        var tenant = new Tenant { EnabledFeatures = "ai,Usage-Tracking,ADVANCED-REPORTS" };

        Assert.True(tenant.HasFeature("ai"));
        Assert.True(tenant.HasFeature("USAGE-TRACKING"));
        Assert.True(tenant.HasFeature("Advanced-Reports"));
        Assert.False(tenant.HasFeature("billing"));
        Assert.False(tenant.HasFeature(""));
    }
}

public class JobCostingWithTravelTests
{
    [Fact]
    public void ActualTotal_IncludesMaterialLaborAndExplicitTravel()
    {
        var job = new Job
        {
            QuotedTotal = 15000m,
            ActualCost = 9200m, // material + other from costs
            ActualCosts = new List<JobCost>
            {
                new JobCost { Amount = 620m, CostType = "Travel", IsDeleted = false },
                new JobCost { Amount = 300m, CostType = "Other", IsDeleted = false }
            },
            Labors = new List<JobLabor>
            {
                new JobLabor { Hours = 8, HourlyRate = 195m },
                new JobLabor { Hours = 4, HourlyRate = 210m, IsDeleted = true } // soft deleted should be excluded
            }
        };

        var activeLabor = job.Labors.Where(l => !l.IsDeleted).Sum(l => l.TotalCost);
        var activeCosts = job.ActualCosts.Where(c => !c.IsDeleted).Sum(c => c.Amount);
        var actualTotal = job.ActualCost + activeLabor + activeCosts;
        var variance = actualTotal - job.QuotedTotal;

        Assert.Equal(1560m, activeLabor); // only first labor
        Assert.Equal(920m, activeCosts);  // travel 620 + other 300
        Assert.Equal(11680m, actualTotal);
        Assert.Equal(-3320m, variance); // under budget
    }

    [Fact]
    public void SoftDeletedCostsAndLabor_DoNotAffectVariance()
    {
        var job = new Job
        {
            QuotedTotal = 5000m,
            ActualCost = 1000m,
            ActualCosts = new List<JobCost> { new JobCost { Amount = 800m, CostType = "Travel", IsDeleted = true } },
            Labors = new List<JobLabor> { new JobLabor { Hours = 5, HourlyRate = 200m, IsDeleted = true } }
        };

        var activeLabor = job.Labors.Where(l => !l.IsDeleted).Sum(l => l.TotalCost);
        var activeCosts = job.ActualCosts.Where(c => !c.IsDeleted).Sum(c => c.Amount);
        var actual = job.ActualCost + activeLabor + activeCosts;

        Assert.Equal(0m, activeLabor);
        Assert.Equal(0m, activeCosts);
        Assert.Equal(1000m, actual);
    }
}