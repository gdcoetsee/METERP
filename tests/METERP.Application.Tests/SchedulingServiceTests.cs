using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class SchedulingServiceTests
{
    private (AppDbContext Db, SchedulingService Service, JobService Jobs, AssetService Assets, EmployeeService Employees, Guid TenantId) CreateHarness()
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
        var jobService = new JobService(db);
        var assetService = new AssetService(db);
        var employeeService = new EmployeeService(db);
        var service = new SchedulingService(jobService, assetService, employeeService);

        return (db, service, jobService, assetService, employeeService, tenantId);
    }

    [Fact]
    public async Task GetBoardAsync_ReturnsJobsAssetsAndEmployees()
    {
        var (db, service, jobService, assetService, employeeService, tenantId) = CreateHarness();
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Sched Co" });
            await db.SaveChangesAsync();

            await jobService.CreateAsync(new Job { CustomerId = customerId, Title = "Panel job", QuotedTotal = 1000m });
            await assetService.CreateAsync(new Asset { CustomerId = customerId, Name = "Van 1", AssetType = "Vehicle" });
            await employeeService.CreateAsync(new Employee { FirstName = "Thabo", LastName = "M", IsActive = true });

            var board = await service.GetBoardAsync();

            Assert.Single(board.Jobs);
            Assert.Single(board.Assets);
            Assert.Single(board.Employees);
        }
    }

    [Fact]
    public async Task AssignJobResourcesAsync_SetsAssetAndNotesEmployee()
    {
        var (db, service, jobService, assetService, employeeService, tenantId) = CreateHarness();
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Sched Co" });
            await db.SaveChangesAsync();

            var jobId = await jobService.CreateAsync(new Job { CustomerId = customerId, Title = "Retrofit", QuotedTotal = 2000m });
            var assetId = await assetService.CreateAsync(new Asset { CustomerId = customerId, Name = "Cherry Picker", AssetType = "Equipment" });
            var employeeId = await employeeService.CreateAsync(new Employee { FirstName = "Johan", LastName = "VDB", IsActive = true });

            await service.AssignJobResourcesAsync(jobId, assetId, employeeId);

            var updated = await jobService.GetByIdAsync(jobId);
            Assert.NotNull(updated);
            Assert.Equal(assetId, updated!.AssetId);
            Assert.Contains("Johan", updated.Notes);
            Assert.Contains("Assigned", updated.Notes);
        }
    }
}