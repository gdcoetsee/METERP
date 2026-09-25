using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class OperationalReportServiceTests
{
    private (AppDbContext Db, OperationalReportService Service, Guid TenantId) Create()
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

        var workforce = new Mock<IWorkforceReportService>();
        workforce.Setup(w => w.GetTechnicianUtilizationAsync(It.IsAny<DateTime?>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TechnicianUtilizationSummary>());
        var cashflow = new Mock<ICashflowReportService>();
        cashflow.Setup(c => c.GetCashflowForecastAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CashflowForecastSummary(0, 0, 0, 0, 0, 0, 0, 0));

        return (db, new OperationalReportService(db, workforce.Object, cashflow.Object), tenantId);
    }

    [Fact]
    public void GetCatalog_IncludesJobPerformanceAndStatusQueues()
    {
        var (_, service, _) = Create();
        var keys = service.GetCatalog().Select(c => c.Key).ToList();
        Assert.Contains("jobs", keys);
        Assert.Contains("jobs-wip", keys);
        Assert.Contains("jobs-awaiting-invoice", keys);
        Assert.Contains("quotes-awaiting-order", keys);
        Assert.Contains("division-performance", keys);
        Assert.Contains("crm-weighted", keys);
        Assert.Contains("crm-followups", keys);
        Assert.Contains("jobs-travel", keys);
        Assert.Contains("ppe-outstanding", keys);
        Assert.True(service.GetCatalog().Count >= 20);
    }

    [Fact]
    public async Task CrmWeighted_SumsOpenPipelineByStage()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            db.Set<Opportunity>().AddRange(
                new Opportunity
                {
                    TenantId = tenantId,
                    Title = "Open",
                    CustomerName = "Acme",
                    Value = 100000m,
                    ProbabilityPercent = 50,
                    Stage = OpportunityStage.Proposal
                },
                new Opportunity
                {
                    TenantId = tenantId,
                    Title = "Won",
                    CustomerName = "Acme",
                    Value = 200000m,
                    ProbabilityPercent = 100,
                    Stage = OpportunityStage.ClosedWon
                });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("crm-weighted");
            Assert.Contains("weighted", table.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Single(table.Rows);
            Assert.Equal("Proposal", table.Rows[0][0]);
            Assert.Equal(50000m.ToString("N2"), table.Rows[0][3]);
        }
    }

    [Fact]
    public async Task CrmFollowUps_ListsOverdueOpenDeals()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            db.Set<Opportunity>().Add(new Opportunity
            {
                TenantId = tenantId,
                Title = "Chase me",
                CustomerName = "Acme",
                Value = 9000m,
                Stage = OpportunityStage.Qualified,
                NextFollowUp = DateTime.UtcNow.Date.AddDays(-2)
            });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("crm-followups");
            Assert.Single(table.Rows);
            Assert.Equal("Chase me", table.Rows[0][0]);
            Assert.Equal("Overdue", table.Rows[0][4]);
        }
    }

    [Fact]
    public async Task JobsWip_ReturnsOnlyInProgress()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            db.Set<Job>().AddRange(
                new Job { TenantId = tenantId, CustomerId = customerId, JobNumber = "J-WIP", Title = "Live", Status = JobStatus.InProgress, QuotedTotal = 1000 },
                new Job { TenantId = tenantId, CustomerId = customerId, JobNumber = "J-SCH", Title = "Later", Status = JobStatus.Scheduled, QuotedTotal = 500 });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("jobs-wip");
            Assert.Single(table.Rows);
            Assert.Contains("J-WIP", table.Rows[0]);
        }
    }

    [Fact]
    public async Task JobPerformance_SplitsTravelLaborAndMargin()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            db.Set<Job>().Add(new Job
            {
                Id = jobId,
                TenantId = tenantId,
                CustomerId = customerId,
                JobNumber = "J-PERF",
                Title = "Substation",
                Status = JobStatus.Completed,
                QuotedTotal = 10_000m,
                CompletedDate = DateTime.UtcNow.Date
            });
            db.Set<JobCost>().Add(new JobCost { TenantId = tenantId, JobId = jobId, CostType = "Travel", Amount = 800m, Description = "Site" });
            db.Set<JobCost>().Add(new JobCost { TenantId = tenantId, JobId = jobId, CostType = "Material", Amount = 2_000m, Description = "Cable" });
            db.Set<JobLabor>().Add(new JobLabor { TenantId = tenantId, JobId = jobId, Hours = 10, HourlyRate = 200m, Technician = "Pat" });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("jobs");
            Assert.Single(table.Rows);
            Assert.Equal("J-PERF", table.Rows[0][0]);

            var detail = await service.GetJobPerformanceAsync(jobId);
            Assert.NotNull(detail);
            Assert.Equal(2_000m, detail!.Labor);
            Assert.Equal(800m, detail.Travel);
            Assert.Equal(2_000m, detail.Materials);
            Assert.Equal(4_800m, detail.Actual);
            Assert.Single(detail.LaborLines);
            Assert.Equal(2, detail.Costs.Count);
        }
    }

    [Fact]
    public async Task DivisionFilter_ExcludesOtherDivisions()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            var divA = Guid.NewGuid();
            var divB = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            db.Set<Division>().AddRange(
                new Division { Id = divA, TenantId = tenantId, Name = "North", Code = "N" },
                new Division { Id = divB, TenantId = tenantId, Name = "South", Code = "S" });
            db.Set<Job>().AddRange(
                new Job { TenantId = tenantId, CustomerId = customerId, DivisionId = divA, JobNumber = "J-N", Title = "North job", Status = JobStatus.InProgress, QuotedTotal = 1 },
                new Job { TenantId = tenantId, CustomerId = customerId, DivisionId = divB, JobNumber = "J-S", Title = "South job", Status = JobStatus.InProgress, QuotedTotal = 1 });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("jobs-wip", divA);
            Assert.Single(table.Rows);
            Assert.Contains("J-N", table.Rows[0]);
        }
    }

    [Fact]
    public async Task QuotesAwaitingOrder_ExcludesConvertedQuotes()
    {
        var (db, service, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            var openId = Guid.NewGuid();
            var wonId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            db.Set<Quote>().AddRange(
                new Quote { Id = openId, TenantId = tenantId, CustomerId = customerId, QuoteNumber = "Q-OPEN", Status = QuoteStatus.Sent, Total = 100 },
                new Quote { Id = wonId, TenantId = tenantId, CustomerId = customerId, QuoteNumber = "Q-JOB", Status = QuoteStatus.Accepted, Total = 200 });
            db.Set<Job>().Add(new Job { TenantId = tenantId, CustomerId = customerId, QuoteId = wonId, JobNumber = "J-1", Title = "From quote", Status = JobStatus.Scheduled });
            await db.SaveChangesAsync();

            var table = await service.RunAsync("quotes-awaiting-order");
            Assert.Single(table.Rows);
            Assert.Equal("Q-OPEN", table.Rows[0][0]);
        }
    }
}
