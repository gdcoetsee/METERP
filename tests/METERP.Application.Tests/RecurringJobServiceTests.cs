using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class RecurringJobServiceTests
{
    private (AppDbContext Db, RecurringJobService Service, JobService Jobs, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var jobs = new JobService(db);
        var service = new RecurringJobService(db, jobs);
        return (db, service, jobs, tenantId);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNextRunTooFarPast()
    {
        var (db, service, _, tenantId) = Create();
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    TenantId = tenantId,
                    CustomerId = customerId,
                    Title = "Old schedule",
                    IntervalDays = 30,
                    NextRunDate = DateTime.UtcNow.Date.AddYears(-2),
                    DefaultQuotedTotal = 1000m
                }));
            Assert.Contains("past", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ProcessDueAsync_SpawnsJobAndAdvancesNextRunDate()
    {
        var (db, service, jobs, tenantId) = Create();
        using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            await service.CreateAsync(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Title = "Monthly inspection",
                IntervalDays = 30,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 5000m
            });

            var spawned = await service.ProcessDueAsync();

            Assert.Equal(1, spawned);
            var allJobs = await jobs.GetAllAsync(pageSize: 50);
            Assert.Contains(allJobs, j => j.Title == "Monthly inspection");

            var schedules = await service.GetAllAsync();
            Assert.Equal(DateTime.UtcNow.Date.AddDays(30), schedules[0].NextRunDate);
        }
    }

    [Fact]
    public async Task CreateAsync_ValidatesTitleCustomerAndInterval()
    {
        var (db, service, _, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    CustomerId = customerId,
                    Title = "  ",
                    IntervalDays = 30
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    CustomerId = customerId,
                    Title = "Valid",
                    IntervalDays = 0
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    CustomerId = customerId,
                    Title = "Too far",
                    IntervalDays = 4000
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    CustomerId = customerId,
                    Title = "Far next run",
                    IntervalDays = 30,
                    NextRunDate = DateTime.UtcNow.Date.AddYears(3)
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new RecurringJobSchedule
                {
                    CustomerId = Guid.NewGuid(),
                    Title = "No customer",
                    IntervalDays = 7
                }));
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsTitleIntervalAndNextRun()
    {
        var (db, service, _, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            var id = await service.CreateAsync(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Title = "Quarterly check",
                IntervalDays = 90,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 2000m
            });

            var schedule = await service.GetByIdAsync(id);
            Assert.NotNull(schedule);
            schedule!.Title = "  Bi-monthly check  ";
            schedule.IntervalDays = 60;
            schedule.NextRunDate = DateTime.UtcNow.Date.AddDays(14);
            schedule.DefaultQuotedTotal = 2500m;

            await service.UpdateAsync(schedule);

            var reloaded = await service.GetByIdAsync(id);
            Assert.Equal("Bi-monthly check", reloaded!.Title);
            Assert.Equal(60, reloaded.IntervalDays);
            Assert.Equal(DateTime.UtcNow.Date.AddDays(14), reloaded.NextRunDate);
            Assert.Equal(2500m, reloaded.DefaultQuotedTotal);
        }
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenScheduleMissing()
    {
        var (db, service, _, _) = Create();
        await using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateAsync(new RecurringJobSchedule
                {
                    Id = Guid.NewGuid(),
                    Title = "Ghost",
                    CustomerId = Guid.NewGuid(),
                    IntervalDays = 30
                }));
        }
    }

    [Fact]
    public async Task ProcessDueAsync_ContinuesWhenOneScheduleFails()
    {
        var (db, service, jobs, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            // Good schedule
            await service.CreateAsync(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Title = "Good schedule",
                IntervalDays = 14,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 1000m
            });

            // Bad schedule — missing customer will fail JobService.Create
            db.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                Title = "Broken schedule",
                IntervalDays = 7,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 500m,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var spawned = await service.ProcessDueAsync();
            Assert.Equal(1, spawned);
            var allJobs = await jobs.GetAllAsync(pageSize: 50);
            Assert.Contains(allJobs, j => j.Title == "Good schedule");
            Assert.DoesNotContain(allJobs, j => j.Title == "Broken schedule");

            var broken = await db.Set<RecurringJobSchedule>().FirstAsync(s => s.Title == "Broken schedule");
            Assert.False(broken.IsActive);
        }
    }

    [Fact]
    public async Task ProcessDueAsync_DeactivatesScheduleWhenCustomerMissing()
    {
        var (db, service, jobs, tenantId) = Create();
        await using (db)
        {
            db.Set<RecurringJobSchedule>().Add(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                Title = "Orphan schedule",
                IntervalDays = 7,
                NextRunDate = DateTime.UtcNow.Date,
                DefaultQuotedTotal = 500m,
                IsActive = true
            });
            await db.SaveChangesAsync();

            Assert.Equal(0, await service.ProcessDueAsync());
            var schedule = await db.Set<RecurringJobSchedule>().FirstAsync(s => s.Title == "Orphan schedule");
            Assert.False(schedule.IsActive);
            Assert.Empty(await jobs.GetAllAsync(pageSize: 50));
        }
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatesSchedule()
    {
        var (db, service, _, tenantId) = Create();
        await using (db)
        {
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Maint Co" });
            await db.SaveChangesAsync();

            var id = await service.CreateAsync(new RecurringJobSchedule
            {
                TenantId = tenantId,
                CustomerId = customerId,
                Title = "Toggle me",
                IntervalDays = 30,
                NextRunDate = DateTime.UtcNow.Date.AddDays(1)
            });

            await service.SetActiveAsync(id, false);
            var active = await service.GetAllAsync(activeOnly: true);
            Assert.Empty(active);

            await service.SetActiveAsync(id, true);
            active = await service.GetAllAsync(activeOnly: true);
            Assert.Single(active);
        }
    }
}