using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PayrollServiceTests
{
    private (AppDbContext Db, PayrollService Service, Guid TenantId) CreateHarness()
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
        return (db, new PayrollService(db), tenantId);
    }

    [Fact]
    public async Task GetMonthlySummariesAsync_AggregatesJobLaborByEmployee()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            var jobId = Guid.NewGuid();
            var thaboId = Guid.NewGuid();
            var johanId = Guid.NewGuid();
            var month = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

            db.Set<Employee>().AddRange(
                new Employee { Id = thaboId, TenantId = tenantId, FirstName = "Thabo", LastName = "Mokoena", JobTitle = "Electrician", DefaultHourlyRate = 195m, IsActive = true },
                new Employee { Id = johanId, TenantId = tenantId, FirstName = "Johan", LastName = "Berg", JobTitle = "Technician", DefaultHourlyRate = 210m, IsActive = true });
            db.Set<Job>().Add(new Job { Id = jobId, TenantId = tenantId, CustomerId = Guid.NewGuid(), Title = "Job", QuotedTotal = 1000m });
            db.Set<JobLabor>().AddRange(
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month, Hours = 8, HourlyRate = 195m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month.AddDays(1), Hours = 4, HourlyRate = 195m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = johanId, WorkDate = month, Hours = 6, HourlyRate = 210m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = null, WorkDate = month, Hours = 99, HourlyRate = 50m },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month.AddMonths(-1), Hours = 40, HourlyRate = 195m, IsDeleted = false },
                new JobLabor { TenantId = tenantId, JobId = jobId, EmployeeId = thaboId, WorkDate = month, Hours = 2, HourlyRate = 195m, IsDeleted = true });
            await db.SaveChangesAsync();

            var summaries = await service.GetMonthlySummariesAsync(month);

            Assert.Equal(2, summaries.Count);

            var thabo = summaries.First(s => s.EmployeeId == thaboId);
            Assert.Equal(12m, thabo.Hours);
            Assert.Equal(2340m, thabo.GrossPay);
            Assert.Equal(2, thabo.LaborEntryCount);

            var johan = summaries.First(s => s.EmployeeId == johanId);
            Assert.Equal(6m, johan.Hours);
            Assert.Equal(1260m, johan.GrossPay);
            Assert.Equal(1, johan.LaborEntryCount);
        }
    }

    [Fact]
    public async Task GetMonthlySummariesAsync_IncludesEmployeesWithZeroLabor()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Set<Employee>().Add(new Employee
            {
                TenantId = tenantId,
                FirstName = "Idle",
                LastName = "Tech",
                DefaultHourlyRate = 180m,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var summaries = await service.GetMonthlySummariesAsync(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Single(summaries);
            Assert.Equal(0m, summaries[0].Hours);
            Assert.Equal(0m, summaries[0].GrossPay);
            Assert.Equal(0, summaries[0].LaborEntryCount);
        }
    }
}