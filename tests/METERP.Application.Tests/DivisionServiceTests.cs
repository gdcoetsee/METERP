using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class DivisionServiceTests
{
    private static (DivisionService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"division-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        return (new DivisionService(db), db, tenantId);
    }

    [Fact]
    public async Task GetAllAsync_ActiveOnly_ExcludesInactive()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            db.Set<Division>().AddRange(
                new Division { TenantId = tenantId, Code = "E", Name = "Electrical", IsActive = true },
                new Division { TenantId = tenantId, Code = "X", Name = "Closed", IsActive = false });
            await db.SaveChangesAsync();

            var active = await service.GetAllAsync(activeOnly: true);
            var all = await service.GetAllAsync(activeOnly: false);

            Assert.Single(active);
            Assert.Equal("Electrical", active[0].Name);
            Assert.Equal(2, all.Count);
        }
    }

    [Fact]
    public async Task CreateAsync_PersistsAndReturnsId()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new Division
            {
                TenantId = tenantId,
                Code = "M",
                Name = "Maintenance"
            });

            var saved = await db.Set<Division>().FirstAsync(d => d.Id == id);
            Assert.Equal("Maintenance", saved.Name);
            Assert.True(saved.IsActive);
        }
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var division = new Division { TenantId = tenantId, Code = "H", Name = "HV" };
            db.Set<Division>().Add(division);
            await db.SaveChangesAsync();

            division.Name = "High Voltage";
            division.IsActive = false;
            await service.UpdateAsync(division);

            var saved = await db.Set<Division>().FirstAsync(d => d.Id == division.Id);
            Assert.Equal("High Voltage", saved.Name);
            Assert.False(saved.IsActive);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsNameTooLong()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new Division
                {
                    TenantId = tenantId,
                    Code = "LONG",
                    Name = new string('X', 101)
                }));
            Assert.Contains("100 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateCode()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            await service.CreateAsync(new Division { TenantId = tenantId, Code = "ELEC", Name = "Electrical" });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new Division { TenantId = tenantId, Code = "elec", Name = "Other" }));
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateName()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            await service.CreateAsync(new Division { TenantId = tenantId, Code = "E1", Name = "Electrical" });
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new Division { TenantId = tenantId, Code = "E2", Name = "Electrical" }));
            Assert.Contains("Electrical", ex.Message);
        }
    }

    [Fact]
    public async Task SetActiveAsync_TogglesVisibility()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new Division { TenantId = tenantId, Code = "CIV", Name = "Civil" });
            await service.SetActiveAsync(id, false);
            var active = await service.GetAllAsync(activeOnly: true);
            Assert.Empty(active);
            await service.SetActiveAsync(id, true);
            active = await service.GetAllAsync(activeOnly: true);
            Assert.Single(active);
        }
    }

    [Fact]
    public async Task SetActiveAsync_ThrowsWhenDeactivatingDivisionWithOpenJobs()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new Division { TenantId = tenantId, Code = "J", Name = "Jobs Div" });
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
            db.Set<Job>().Add(new Job
            {
                TenantId = tenantId,
                CustomerId = customerId,
                DivisionId = id,
                JobNumber = "J-DIV",
                Title = "Open",
                Status = JobStatus.InProgress
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetActiveAsync(id, false));
            Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True((await service.GetByIdAsync(id))!.IsActive);
        }
    }

    [Fact]
    public async Task SetActiveAsync_ThrowsWhenDeactivatingDivisionWithActiveEmployees()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new Division { TenantId = tenantId, Code = "HR", Name = "HR Div" });
            db.Set<Employee>().Add(new Employee
            {
                TenantId = tenantId,
                DivisionId = id,
                EmployeeNumber = "EMP-DIV-1",
                FirstName = "Pat",
                LastName = "Lee",
                IsActive = true
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SetActiveAsync(id, false));
            Assert.Contains("active employees", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True((await service.GetByIdAsync(id))!.IsActive);
        }
    }
}