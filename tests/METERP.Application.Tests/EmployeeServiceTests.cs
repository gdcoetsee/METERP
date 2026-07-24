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
    public async Task UpdateAsync_PersistsRateAndContactFields()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var id = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-UPD",
            FirstName = "Lerato",
            LastName = "Dlamini",
            DefaultHourlyRate = 175m,
            Email = "old@test.com",
            LinkedUserId = Guid.NewGuid(),
            LeaveBalanceDays = 8m,
            MandatoryHoursPerMonth = 140m
        });

        var employee = await service.GetByIdAsync(id);
        Assert.NotNull(employee);
        var linked = employee!.LinkedUserId;
        employee.DefaultHourlyRate = 210m;
        employee.Email = "lerato@field.demo";
        employee.JobTitle = "Senior Technician";
        employee.Phone = "0820000000";
        employee.MandatoryHoursPerMonth = 150m;
        await service.UpdateAsync(employee);

        var reloaded = await service.GetByIdAsync(id);
        Assert.Equal(210m, reloaded!.DefaultHourlyRate);
        Assert.Equal("lerato@field.demo", reloaded.Email);
        Assert.Equal("Senior Technician", reloaded.JobTitle);
        Assert.Equal("0820000000", reloaded.Phone);
        Assert.Equal(linked, reloaded.LinkedUserId);
        Assert.Equal(8m, reloaded.LeaveBalanceDays);
        Assert.Equal(150m, reloaded.MandatoryHoursPerMonth);
    }

    [Fact]
    public async Task GetAllAsync_IncludeInactive_ReturnsInactiveStaff()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        await service.CreateAsync(new Employee { EmployeeNumber = "E1", FirstName = "Active", LastName = "Tech", IsActive = true });
        await service.CreateAsync(new Employee { EmployeeNumber = "E2", FirstName = "Former", LastName = "Tech", IsActive = false });

        var activeOnly = await service.GetAllAsync();
        var all = await service.GetAllAsync(includeInactive: true);

        Assert.Single(activeOnly);
        Assert.Equal(2, all.Count);
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

    [Fact]
    public async Task DeleteAsync_ThrowsWhenEmployeeAssignedToOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var empId = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-JOB",
            FirstName = "Busy",
            LastName = "Tech"
        });
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        db.Set<Job>().Add(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            AssignedEmployeeId = empId,
            JobNumber = "J-1",
            Title = "Open",
            Status = JobStatus.InProgress
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(empId));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenEmployeeHasPendingLeave()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var empId = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-LV",
            FirstName = "Leave",
            LastName = "Tech"
        });
        db.Set<LeaveRequest>().Add(new LeaveRequest
        {
            TenantId = tenantId,
            EmployeeId = empId,
            StartDate = DateTime.UtcNow.Date.AddDays(7),
            EndDate = DateTime.UtcNow.Date.AddDays(10),
            DaysRequested = 3,
            Status = LeaveRequestStatus.PendingManager
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(empId));
        Assert.Contains("leave", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenHourlyRateNegative()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-NEG",
                FirstName = "Bad",
                LastName = "Rate",
                DefaultHourlyRate = -10m
            }));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmployeeNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-DUP",
            FirstName = "One",
            LastName = "Tech"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-DUP",
                FirstName = "Two",
                LastName = "Tech"
            }));
    }

    [Fact]
    public async Task CreateAsync_AssignsEmployeeNumber_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var id = await service.CreateAsync(new Employee
        {
            FirstName = "Auto",
            LastName = "Number"
        });

        var emp = await service.GetByIdAsync(id);
        Assert.False(string.IsNullOrWhiteSpace(emp!.EmployeeNumber));
        Assert.StartsWith("EMP-", emp.EmployeeNumber);
    }

    [Fact]
    public async Task SetActiveAsync_ThrowsWhenDeactivatingEmployeeOnOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var empId = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-DA",
            FirstName = "Active",
            LastName = "Lead"
        });
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        db.Set<Job>().Add(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            AssignedEmployeeId = empId,
            JobNumber = "J-DA",
            Title = "Open",
            Status = JobStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetActiveAsync(empId, false));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True((await service.GetByIdAsync(empId))!.IsActive);
    }
}