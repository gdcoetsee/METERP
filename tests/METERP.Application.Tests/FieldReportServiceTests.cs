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
}