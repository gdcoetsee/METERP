using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Integrations;
using METERP.Infrastructure.Persistence;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class BillingWebhookMaintenanceServiceTests
{
    private (AppDbContext Db, BillingWebhookMaintenanceService Service) CreateHarness()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        return (db, new BillingWebhookMaintenanceService(db));
    }

    [Fact]
    public async Task PurgeProcessedEventsOlderThanAsync_RemovesStaleRecordsOnly()
    {
        var (db, service) = CreateHarness();
        using (db)
        {
            db.ProcessedStripeWebhookEvents.AddRange(
                new ProcessedStripeWebhookEvent
                {
                    EventId = "evt_old",
                    EventType = "customer.subscription.updated",
                    ProcessedAtUtc = DateTime.UtcNow.AddDays(-120)
                },
                new ProcessedStripeWebhookEvent
                {
                    EventId = "evt_recent",
                    EventType = "customer.subscription.updated",
                    ProcessedAtUtc = DateTime.UtcNow.AddDays(-2)
                });
            await db.SaveChangesAsync();

            var removed = await service.PurgeProcessedEventsOlderThanAsync(TimeSpan.FromDays(90));

            Assert.Equal(1, removed);
            Assert.Single(db.ProcessedStripeWebhookEvents);
            Assert.Equal("evt_recent", db.ProcessedStripeWebhookEvents.Single().EventId);
        }
    }

    [Fact]
    public async Task PurgeProcessedEventsOlderThanAsync_ReturnsZeroForNonPositiveRetention()
    {
        var (db, service) = CreateHarness();
        using (db)
        {
            db.ProcessedStripeWebhookEvents.Add(new ProcessedStripeWebhookEvent
            {
                EventId = "evt_keep",
                EventType = "ignored",
                ProcessedAtUtc = DateTime.UtcNow.AddYears(-1)
            });
            await db.SaveChangesAsync();

            var removed = await service.PurgeProcessedEventsOlderThanAsync(TimeSpan.Zero);

            Assert.Equal(0, removed);
            Assert.Single(db.ProcessedStripeWebhookEvents);
        }
    }
}