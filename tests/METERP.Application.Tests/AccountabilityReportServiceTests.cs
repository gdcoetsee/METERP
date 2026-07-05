using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AccountabilityReportServiceTests
{
    private (AppDbContext Db, AccountabilityReportService Service) Create(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, new AccountabilityReportService(db, tenantProvider.Object));
    }

    [Fact]
    public async Task GetDivisionScorecardsAsync_AggregatesJobsPerDivision()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = Create(tenantId);
        using (db)
        {
            var divisionId = Guid.NewGuid();
            db.Set<Division>().Add(new Division
            {
                Id = divisionId,
                TenantId = tenantId,
                Code = "ELEC",
                Name = "Electrical",
                IsActive = true
            });

            var jobId = Guid.NewGuid();
            db.Set<Job>().Add(new Job
            {
                Id = jobId,
                TenantId = tenantId,
                DivisionId = divisionId,
                JobNumber = "J-100",
                Title = "Panel install",
                Status = JobStatus.InProgress,
                QuotedTotal = 25000m,
                SignOffStatus = JobSignOffStatus.SignedOff,
                Milestones =
                {
                    new JobMilestone { TenantId = tenantId, Title = "Survey", PercentComplete = 100, Status = JobMilestoneStatus.Done },
                    new JobMilestone { TenantId = tenantId, Title = "Install", PercentComplete = 50, Status = JobMilestoneStatus.InProgress }
                }
            });

            db.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantId,
                JobId = jobId,
                InvoiceNumber = "INV-1",
                Status = InvoiceStatus.Paid,
                Total = 20000m,
                InvoiceDate = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            var rows = await service.GetDivisionScorecardsAsync();

            Assert.Single(rows);
            var row = rows[0];
            Assert.Equal("Electrical", row.DivisionName);
            Assert.Equal(1, row.ActiveJobs);
            Assert.Equal(75m, row.AvgProgressPercent);
            Assert.Equal(1, row.ReadyToInvoiceCount);
            Assert.Equal(25000m, row.ReadyToInvoiceValue);
            Assert.Equal(20000m, row.InvoicedRevenue);
        }
    }

    [Fact]
    public async Task ExportDivisionScorecardsCsvAsync_IncludesHeaderAndRow()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = Create(tenantId);
        using (db)
        {
            db.Set<Division>().Add(new Division
            {
                TenantId = tenantId,
                Code = "GEN",
                Name = "General",
                IsActive = true
            });
            await db.SaveChangesAsync();

            var csv = await service.ExportDivisionScorecardsCsvAsync();

            Assert.Contains("DivisionCode,DivisionName", csv);
            Assert.Contains("GEN", csv);
            Assert.Contains("General", csv);
        }
    }

    [Fact]
    public async Task GetUserActivityAsync_GroupsAuditByUser()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = Create(tenantId);
        using (db)
        {
            db.Set<AuditLogEntry>().AddRange(
                new AuditLogEntry
                {
                    TenantId = tenantId,
                    UserEmail = "exec@acme.demo",
                    Action = "APPROVE",
                    EntityType = "Quote",
                    EntityReference = "Q-1",
                    OccurredAtUtc = DateTime.UtcNow.AddDays(-1)
                },
                new AuditLogEntry
                {
                    TenantId = tenantId,
                    UserEmail = "exec@acme.demo",
                    Action = "UPDATE",
                    EntityType = "Job",
                    EntityReference = "J-1",
                    OccurredAtUtc = DateTime.UtcNow.AddHours(-2)
                },
                new AuditLogEntry
                {
                    TenantId = tenantId,
                    UserEmail = "tech@acme.demo",
                    Action = "CREATE",
                    EntityType = "FieldReport",
                    EntityReference = "FR-1",
                    OccurredAtUtc = DateTime.UtcNow.AddDays(-3)
                });
            await db.SaveChangesAsync();

            var rows = await service.GetUserActivityAsync(30);

            Assert.Equal(2, rows.Count);
            var exec = rows.First(r => r.UserEmail == "exec@acme.demo");
            Assert.Equal(2, exec.TotalActions);
            Assert.Equal(1, exec.ApprovalActions);
        }
    }

    [Fact]
    public async Task ExportOverdueApprovalsCsvAsync_IncludesOverdueQuote()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = Create(tenantId);
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Csv Co" };
            db.Set<Customer>().Add(customer);
            await db.SaveChangesAsync();

            db.Set<Quote>().Add(new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-CSV-001",
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                SubmittedForApprovalAt = DateTime.UtcNow.AddHours(-60)
            });
            await db.SaveChangesAsync();

            var csv = await service.ExportOverdueApprovalsCsvAsync();

            Assert.Contains("ItemType,Reference", csv);
            Assert.Contains("Q-CSV-001", csv);
            Assert.Contains("Quote", csv);
        }
    }

    [Fact]
    public async Task GetOverdueApprovalsAsync_FlagsQuotePastSla()
    {
        var tenantId = Guid.NewGuid();
        var (db, service) = Create(tenantId);
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Overdue Co" };
            db.Set<Customer>().Add(customer);
            await db.SaveChangesAsync();

            db.Set<Quote>().Add(new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-OVERDUE",
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                SubmittedForApprovalAt = DateTime.UtcNow.AddHours(-72)
            });
            await db.SaveChangesAsync();

            Assert.Single(await db.Set<Quote>().ToListAsync());

            var rows = await service.GetOverdueApprovalsAsync();

            Assert.Single(rows);
            Assert.Equal("Quote", rows[0].ItemType);
            Assert.Equal("Q-OVERDUE", rows[0].Reference);
            Assert.True(rows[0].HoursInQueue >= 48);
            Assert.Equal(48, rows[0].SlaHours);
        }
    }
}