using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Tests for Job service and costing (labor + explicit travel costs).
/// Core to contractor use-case: variance tracking with travel explicit.
/// Follows full-testing rules: entity pure methods + service behavior + counters + soft deletes.
/// </summary>
public class JobTests
{
    private AppDbContext CreateInMemoryContext(Guid? fixedTenantId = null)
    {
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(fixedTenantId ?? Guid.NewGuid());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(s => s.UserName).Returns("test-user");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProviderMock.Object, currentUserMock.Object);
    }

    [Fact]
    public void Job_GetActualTotal_IncludesMaterialLaborAndExplicitTravel()
    {
        var job = new Job
        {
            QuotedTotal = 15000m,
            ActualCost = 0m, // base not used in Get; use JobCost entries for explicit tracking
            ActualCosts = new List<JobCost>
            {
                new JobCost { Amount = 9200m, CostType = "Material", IsDeleted = false }, // explicit material as cost
                new JobCost { Amount = 620m, CostType = "Travel", IsDeleted = false },
                new JobCost { Amount = 300m, CostType = "Other", IsDeleted = false }
            },
            Labors = new List<JobLabor>
            {
                new JobLabor { Hours = 8, HourlyRate = 195m, IsDeleted = false },
                new JobLabor { Hours = 4, HourlyRate = 210m, IsDeleted = true } // soft deleted
            }
        };

        var actual = job.GetActualTotal();
        var variance = job.GetVariance();

        Assert.Equal(9200m + 620m + 300m + 1560m, actual); // material + travel + other + active labor
        Assert.Equal(-3320m, variance); // under budget
    }

    [Fact]
    public void Job_GetActualTotal_ExcludesSoftDeletedCostsAndLabor()
    {
        var job = new Job
        {
            QuotedTotal = 5000m,
            ActualCost = 1000m,
            ActualCosts = new List<JobCost> { new JobCost { Amount = 800m, CostType = "Travel", IsDeleted = true } },
            Labors = new List<JobLabor> { new JobLabor { Hours = 5, HourlyRate = 200m, IsDeleted = true } }
        };

        var actual = job.GetActualTotal();
        var variance = job.GetVariance();

        Assert.Equal(0m, actual); // base not included in Get; only active tracked costs + labor (both soft-deleted here)
        Assert.Equal(-5000m, variance);
    }

    [Fact]
    public async Task JobService_CreateAsync_IncrementsTenantJobCounter()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(t => t.IncrementJobCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                         .Returns(Task.CompletedTask);

        var service = new JobService(db, tenantServiceMock.Object);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            JobNumber = "",
            Title = "Test Job with Travel",
            QuotedTotal = 10000m
        };

        var id = await service.CreateAsync(job);

        tenantServiceMock.Verify(t => t.IncrementJobCountAsync(tenantId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task JobService_AddCostAsync_TravelCost_UpdatesAndExposesViaGetActualTotal()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 10000m,
            ActualCost = 0m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        var travelCost = new JobCost
        {
            JobId = job.Id,
            Description = "Site travel",
            Amount = 1500m,
            CostType = "Travel",
            IsDeleted = false
        };

        await service.AddCostAsync(travelCost);

        var reloaded = await db.Set<Job>()
            .Include(j => j.ActualCosts)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(1500m, reloaded.GetActualTotal()); // base 0 + travel
        Assert.Equal("Travel", reloaded.ActualCosts.First().CostType);
    }

    [Fact]
    public async Task JobService_AddLaborAsync_And_Delete_ExcludesFromActualTotal()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 5000m,
            ActualCost = 0m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        var labor = new JobLabor
        {
            JobId = job.Id,
            Hours = 10,
            HourlyRate = 200m,
            Description = "Technician time",
            IsDeleted = false
        };

        await service.AddLaborAsync(labor);

        var reloaded = await db.Set<Job>()
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(2000m, reloaded.GetActualTotal());

        // Now soft delete the labor
        await service.DeleteLaborAsync(labor.Id);

        var afterDelete = await db.Set<Job>()
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal(0m, afterDelete.GetActualTotal());
    }

    [Fact]
    public async Task JobService_AddLaborAsync_LinksEmployee_DefaultsTechnicianAndRate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var employeeId = Guid.NewGuid();
        db.Set<Employee>().Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            FirstName = "Thabo",
            LastName = "Mokoena",
            DefaultHourlyRate = 195m
        });

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 5000m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);
        await service.AddLaborAsync(new JobLabor
        {
            JobId = job.Id,
            EmployeeId = employeeId,
            Hours = 6
        });

        var labor = await db.Set<JobLabor>().FirstAsync(l => l.JobId == job.Id);
        Assert.Equal(employeeId, labor.EmployeeId);
        Assert.Equal("Thabo Mokoena", labor.Technician);
        Assert.Equal(195m, labor.HourlyRate);
        Assert.Equal(1170m, labor.TotalCost);
    }

    [Fact]
    public async Task JobService_AddCost_And_Labor_TravelExplicit_InVariance()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            QuotedTotal = 10000m,
            ActualCost = 3000m // some base material
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db, null);

        // Add explicit travel cost
        await service.AddCostAsync(new JobCost { JobId = job.Id, Amount = 1200m, CostType = "Travel" });

        // Add labor
        await service.AddLaborAsync(new JobLabor { JobId = job.Id, Hours = 8, HourlyRate = 150m });

        var reloaded = await db.Set<Job>()
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors)
            .FirstAsync(j => j.Id == job.Id);

        var actual = reloaded.GetActualTotal();
        var variance = reloaded.GetVariance();

        // tracked costs (travel) + labor (base overwritten by service to costs sum, Get uses tracked)
        Assert.Equal(2400m, actual); // 1200 travel + 1200 labor
        Assert.Equal(-7600m, variance); // under
    }
}
