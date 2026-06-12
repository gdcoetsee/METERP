using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class EmployeeServiceTests
{
    private AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task CreateAsync_PersistsEmployee()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var emp = new Employee
        {
            EmployeeNumber = "EMP-001",
            FirstName = "Thabo",
            LastName = "Mokoena",
            DefaultHourlyRate = 185m
        };

        var id = await service.CreateAsync(emp);

        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Thabo", loaded.FirstName);
        Assert.Equal(tenantId, loaded.TenantId);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesInactiveEmployees()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        await service.CreateAsync(new Employee { EmployeeNumber = "E1", FirstName = "Active", LastName = "Tech", IsActive = true });
        await service.CreateAsync(new Employee { EmployeeNumber = "E2", FirstName = "Former", LastName = "Tech", IsActive = false });

        var results = await service.GetAllAsync();

        Assert.Single(results);
        Assert.Equal("Active", results[0].FirstName);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        await service.CreateAsync(new Employee { EmployeeNumber = "E-100", FirstName = "Sarah", LastName = "Naidoo" });
        await service.CreateAsync(new Employee { EmployeeNumber = "E-200", FirstName = "John", LastName = "Smith" });

        var results = await service.GetAllAsync("naidoo");

        Assert.Single(results);
        Assert.Equal("Sarah", results[0].FirstName);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesEmployee()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var id = await service.CreateAsync(new Employee { EmployeeNumber = "E-DEL", FirstName = "Del", LastName = "Me" });

        await service.DeleteAsync(id);

        Assert.Null(await service.GetByIdAsync(id));
        var deleted = await db.Set<Employee>().IgnoreQueryFilters().FirstAsync(e => e.Id == id);
        Assert.True(deleted.IsDeleted);
    }
}