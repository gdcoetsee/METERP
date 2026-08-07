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
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                FirstName = "Note",
                LastName = "Long",
                Notes = new string('N', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsNotesAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var id = await service.CreateAsync(new Employee
        {
            FirstName = "Note",
            LastName = "Ok",
            Notes = new string('N', 2000),
            IsActive = true
        });
        var saved = await db.Set<Employee>().FirstAsync(e => e.Id == id);
        Assert.Equal(2000, saved.Notes!.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmployeeNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = new string('E', 51),
                FirstName = "Num",
                LastName = "Long"
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsEmployeeNumberAt50Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var id = await service.CreateAsync(new Employee
        {
            EmployeeNumber = new string('E', 50),
            FirstName = "Num",
            LastName = "Ok",
            IsActive = true
        });
        var saved = await db.Set<Employee>().FirstAsync(e => e.Id == id);
        Assert.Equal(50, saved.EmployeeNumber.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenJobTitleTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                FirstName = "Title",
                LastName = "Long",
                JobTitle = new string('J', 101)
            }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsJobTitleAt100Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var id = await service.CreateAsync(new Employee
        {
            FirstName = "Title",
            LastName = "Ok",
            JobTitle = new string('J', 100),
            IsActive = true
        });
        var saved = await db.Set<Employee>().FirstAsync(e => e.Id == id);
        Assert.Equal(100, saved.JobTitle!.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-LONG",
                FirstName = new string('A', 101),
                LastName = "Ok"
            }));
        Assert.Contains("100 characters", ex.Message);
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
    public async Task CreateAsync_ThrowsWhenHourlyRateTooHigh()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-RATE",
                FirstName = "Hi",
                LastName = "Rate",
                DefaultHourlyRate = 50_001m
            }));
        Assert.Contains("50,000", ex.Message);
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
    public async Task CreateAsync_ThrowsWhenManagerMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-MGR",
                FirstName = "Has",
                LastName = "Boss",
                ManagerEmployeeId = Guid.NewGuid()
            }));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDivisionMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-DIV",
                FirstName = "Div",
                LastName = "Missing",
                DivisionId = Guid.NewGuid()
            }));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenEmployeeIsOwnManager()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var id = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-SELF",
            FirstName = "Self",
            LastName = "Mgr"
        });
        var emp = await service.GetByIdAsync(id);
        Assert.NotNull(emp);
        emp!.ManagerEmployeeId = id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(emp));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenLeaveBalanceNegative()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-LVN",
                FirstName = "Bad",
                LastName = "Leave",
                LeaveBalanceDays = -1m
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
    public async Task CreateAsync_ThrowsWhenEmailDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-MAIL1",
            FirstName = "One",
            LastName = "Tech",
            Email = "shared@acme.demo"
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-MAIL2",
                FirstName = "Two",
                LastName = "Tech",
                Email = "shared@acme.demo"
            }));
        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                FirstName = "Email",
                LastName = "Long",
                Email = new string('a', 195) + "@x.com"
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPhoneTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                FirstName = "Phone",
                LastName = "Long",
                Phone = new string('1', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailInvalid()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-BADMAIL",
                FirstName = "Bad",
                LastName = "Mail",
                Email = "not-an-email"
            }));
        Assert.Contains("valid address", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenHireDateTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-FUTURE",
                FirstName = "Future",
                LastName = "Hire",
                HireDate = DateTime.UtcNow.Date.AddDays(60)
            }));
        Assert.Contains("Hire date", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenLinkedUserAlreadyAssigned()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var userId = Guid.NewGuid();
        await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-LINK1",
            FirstName = "First",
            LastName = "Link",
            LinkedUserId = userId
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Employee
            {
                EmployeeNumber = "E-LINK2",
                FirstName = "Second",
                LastName = "Link",
                LinkedUserId = userId
            }));
        Assert.Contains("already linked", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task SetActiveAsync_ThrowsWhenDeactivatingCrewMemberOnOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var empId = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-CREW",
            FirstName = "Crew",
            LastName = "Tech"
        });
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-CREW",
            Title = "Open",
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        db.Set<JobCrewAssignment>().Add(new JobCrewAssignment
        {
            TenantId = tenantId,
            JobId = job.Id,
            EmployeeId = empId
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetActiveAsync(empId, false));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenEmployeeIsCrewOnOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new EmployeeService(db);
        var empId = await service.CreateAsync(new Employee
        {
            EmployeeNumber = "E-CREW-DEL",
            FirstName = "Crew",
            LastName = "Del"
        });
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-CREW-DEL",
            Title = "Open",
            Status = JobStatus.Scheduled
        };
        db.Set<Job>().Add(job);
        db.Set<JobCrewAssignment>().Add(new JobCrewAssignment
        {
            TenantId = tenantId,
            JobId = job.Id,
            EmployeeId = empId
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(empId));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}