using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class WorkforceReportServiceTests
{
    private (AppDbContext Db, WorkforceReportService Service, Guid TenantId) CreateHarness()
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
        return (db, new WorkforceReportService(db), tenantId);
    }

    [Fact]
    public async Task GetTechnicianUtilizationAsync_ComputesPercentFromJobLabor()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var jobId = Guid.NewGuid();
            var techId = Guid.NewGuid();
            var month = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

            db.Set<Employee>().Add(new Employee
            {
                Id = techId,
                TenantId = tenantId,
                FirstName = "Thabo",
                LastName = "M",
                IsActive = true
            });
            db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "J", QuotedTotal = 100m });
            db.Set<JobLabor>().Add(new JobLabor
            {
                TenantId = tenantId,
                JobId = jobId,
                EmployeeId = techId,
                WorkDate = month,
                Hours = 80,
                HourlyRate = 195m
            });
            await db.SaveChangesAsync();

            var results = await service.GetTechnicianUtilizationAsync(month, monthlyCapacityHours: 160m);

            Assert.Single(results);
            Assert.Equal(80m, results[0].HoursLogged);
            Assert.Equal(50m, results[0].UtilizationPercent);
        }
    }

    [Fact]
    public async Task GetTechnicianUtilizationAsync_ExcludesInactiveEmployees()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Set<Employee>().AddRange(
                new Employee { TenantId = tenantId, FirstName = "Active", LastName = "Tech", IsActive = true },
                new Employee { TenantId = tenantId, FirstName = "Former", LastName = "Tech", IsActive = false });
            await db.SaveChangesAsync();

            var results = await service.GetTechnicianUtilizationAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Single(results);
            Assert.Equal("Active Tech", results[0].Name);
        }
    }
}