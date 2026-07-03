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
}