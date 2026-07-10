using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class JobEmergencyAndBillingTermsTests
{
    private static (JobService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"job-emg-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (new JobService(db, tenantProvider: tenantProvider.Object), db, tenantId);
    }

    [Fact]
    public async Task CreateEmergencyAsync_CreatesInProgressJobWithoutQuote()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Callout Co" };
            db.Set<Customer>().Add(customer);
            await db.SaveChangesAsync();

            var jobId = await service.CreateEmergencyAsync(
                customer.Id,
                "After-hours fault",
                "Generator trip",
                quotedEstimate: 4500m,
                depositPercent: 50m,
                retentionPercent: 5m);

            var job = await service.GetByIdAsync(jobId);
            Assert.NotNull(job);
            Assert.True(job!.IsEmergency);
            Assert.Null(job.QuoteId);
            Assert.Equal(JobStatus.InProgress, job.Status);
            Assert.Equal(4500m, job.QuotedTotal);
            Assert.Equal(50m, job.DepositPercent);
            Assert.Equal(5m, job.RetentionPercent);
            Assert.Contains("Emergency", job.Notes);
        }
    }

    [Fact]
    public async Task CreateEmergencyAsync_RequiresCustomerAndTitle()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateEmergencyAsync(Guid.Empty, "T", null, 0));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateEmergencyAsync(Guid.NewGuid(), "  ", null, 0));
        }
    }

    [Fact]
    public async Task UpdateBillingTermsAsync_UpdatesOpenJob()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Terms Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Install",
                QuotedTotal = 1000m,
                DepositPercent = 30m,
                RetentionPercent = 10m,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            await service.UpdateBillingTermsAsync(job.Id, 40m, 12.5m);

            var reloaded = await service.GetByIdAsync(job.Id);
            Assert.Equal(40m, reloaded!.DepositPercent);
            Assert.Equal(12.5m, reloaded.RetentionPercent);
        }
    }

    [Fact]
    public async Task UpdateBillingTermsAsync_BlockedWhenClosed()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Closed Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Done",
                Status = JobStatus.Closed,
                ClosedAt = DateTime.UtcNow
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateBillingTermsAsync(job.Id, 20m, 5m));
        }
    }
}
