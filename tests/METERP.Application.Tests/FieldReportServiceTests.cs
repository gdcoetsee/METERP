using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class FieldReportServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private (AppDbContext Db, FieldReportService Service, JobService Jobs) CreateServices(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var jobs = new JobService(db);
        var service = new FieldReportService(db, jobs);
        return (db, service, jobs);
    }

    [Fact]
    public async Task ApproveAsync_PostsLaborAndTravelToJob()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var employeeId = Guid.NewGuid();
            db.Set<Employee>().Add(new Employee
            {
                Id = employeeId,
                TenantId = tenantId,
                FirstName = "Tech",
                LastName = "One",
                DefaultHourlyRate = 200m
            });

            var jobId = await jobs.CreateAsync(new Job
            {
                CustomerId = customerId,
                Title = "Install",
                QuotedTotal = 5000m,
                AssignedEmployeeId = employeeId
            });

            var reportId = await service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 6m,
                TravelCost = 450m
            });

            var approved = await service.ApproveAsync(reportId, TestUserId);
            Assert.True(approved);

            var job = await jobs.GetByIdAsync(jobId);
            Assert.Single(job!.Labors);
            Assert.Equal(6m, job.Labors.First().Hours);
            Assert.Equal(1200m, job.Labors.First().TotalCost);

            var travel = job.ActualCosts.First(c => c.CostType == "Travel");
            Assert.Equal(450m, travel.Amount);
        }
    }

    [Fact]
    public async Task SubmitAsync_SetsPendingApproval()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

            var reportId = await service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 7m,
                TravelCost = 120m
            });

            var saved = await db.Set<FieldReport>().FirstAsync(r => r.Id == reportId);
            Assert.Equal(FieldReportStatus.PendingApproval, saved.Status);
            Assert.True(saved.SubmittedAt > DateTime.UtcNow.AddMinutes(-1));
        }
    }

    [Fact]
    public async Task SubmitAsync_ThrowsWhenJobMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(new FieldReport
            {
                JobId = Guid.NewGuid(),
                SubmittedByUserId = TestUserId,
                HoursWorked = 4m
            }));
        }
    }

    [Fact]
    public async Task SubmitAsync_ThrowsWhenNegativeValues()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = -1m
            }));
        }
    }

    [Fact]
    public async Task GetPendingAsync_ReturnsOnlyPendingReports()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

            var pendingId = await service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 5m
            });

            db.Set<FieldReport>().Add(new FieldReport
            {
                TenantId = tenantId,
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 3m,
                Status = FieldReportStatus.Approved,
                SubmittedAt = DateTime.UtcNow.AddDays(-1)
            });
            await db.SaveChangesAsync();

            var pending = await service.GetPendingAsync();
            Assert.Single(pending);
            Assert.Equal(pendingId, pending[0].Id);
        }
    }

    [Fact]
    public async Task ApproveAsync_ReturnsFalse_WhenAlreadyApproved()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

            var reportId = await service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 4m
            });

            Assert.True(await service.ApproveAsync(reportId, TestUserId));
            Assert.False(await service.ApproveAsync(reportId, TestUserId));
        }
    }

    [Fact]
    public async Task RejectAsync_SetsRejectedStatus_AndDoesNotPostCosts()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, jobs) = CreateServices(tenantId);
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });
            var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

            var reportId = await service.SubmitAsync(new FieldReport
            {
                JobId = jobId,
                SubmittedByUserId = TestUserId,
                HoursWorked = 8m,
                TravelCost = 200m
            });

            Assert.True(await service.RejectAsync(reportId, TestUserId, "Incorrect hours"));

            var report = await db.Set<FieldReport>().FirstAsync(r => r.Id == reportId);
            Assert.Equal(FieldReportStatus.Rejected, report.Status);
            Assert.Equal("Incorrect hours", report.RejectionReason);

            var job = await jobs.GetByIdAsync(jobId);
            Assert.Empty(job!.Labors);
            Assert.Empty(job.ActualCosts);
        }
    }
}