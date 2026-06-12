using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class JobReportServiceTests
{
    private (AppDbContext Db, JobReportService Service, Guid TenantId) CreateHarness()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, new JobReportService(db), tenantId);
    }

    [Fact]
    public async Task GetJobProfitabilitySummaryAsync_ComputesAverageAndTopPerformer()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var customerId = Guid.NewGuid();
            var underBudgetId = Guid.NewGuid();
            var overBudgetId = Guid.NewGuid();

            db.Set<Job>().AddRange(
                new Job
                {
                    Id = underBudgetId,
                    TenantId = tenantId,
                    CustomerId = customerId,
                    JobNumber = "J-001",
                    Title = "Mine Install",
                    QuotedTotal = 10_000m
                },
                new Job
                {
                    Id = overBudgetId,
                    TenantId = tenantId,
                    CustomerId = customerId,
                    JobNumber = "J-002",
                    Title = "Overrun Job",
                    QuotedTotal = 5_000m
                });

            db.Set<JobCost>().AddRange(
                new JobCost { TenantId = tenantId, JobId = underBudgetId, Amount = 7_800m, CostType = "Material" },
                new JobCost { TenantId = tenantId, JobId = overBudgetId, Amount = 5_500m, CostType = "Material" });

            await db.SaveChangesAsync();

            var summary = await service.GetJobProfitabilitySummaryAsync();

            Assert.Equal(2, summary.JobsAnalyzed);
            Assert.Equal(6m, summary.AverageMarginPercent); // (22% + -10%) / 2
            Assert.NotNull(summary.TopPerformer);
            Assert.Equal("Mine Install", summary.TopPerformer!.Title);
            Assert.Equal(22m, summary.TopPerformer.MarginPercent);
        }
    }

    [Fact]
    public async Task GetJobProfitabilitySummaryAsync_ExcludesJobsWithoutRecordedCosts()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Set<Job>().Add(new Job
            {
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                Title = "Quoted Only",
                QuotedTotal = 8_000m
            });
            await db.SaveChangesAsync();

            var summary = await service.GetJobProfitabilitySummaryAsync();

            Assert.Equal(0, summary.JobsAnalyzed);
            Assert.Equal(0m, summary.AverageMarginPercent);
            Assert.Null(summary.TopPerformer);
        }
    }

    [Fact]
    public void Job_GetMarginPercent_UsesQuotedMinusActual()
    {
        var job = new Job
        {
            QuotedTotal = 4_000m,
            ActualCosts =
            {
                new JobCost { Amount = 1_500m, CostType = "Travel" },
                new JobCost { Amount = 1_000m, CostType = "Material" }
            },
            Labors =
            {
                new JobLabor { Hours = 4, HourlyRate = 200m }
            }
        };

        Assert.Equal(3_300m, job.GetActualTotal());
        Assert.Equal(17.5m, job.GetMarginPercent());
    }
}