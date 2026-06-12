using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantBillingViewServiceTests
{
    private (AppDbContext Db, TenantBillingViewService Service, Guid TenantId) CreateHarness()
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
        return (db, new TenantBillingViewService(db), tenantId);
    }

    [Fact]
    public async Task GetAsync_ReturnsLatestTierAndFeatureFlags()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Billing Co",
                Subdomain = "billingco",
                Tier = SubscriptionTier.Professional,
                SubscriptionStatus = "active",
                EnabledFeatures = "ai,usage-tracking"
            });
            await db.SaveChangesAsync();

            var view = await service.GetAsync(tenantId);

            Assert.NotNull(view);
            Assert.Equal(SubscriptionTier.Professional, view!.Tenant.Tier);
            Assert.True(view.HasAiFeature);
            Assert.False(view.IsPastDue);
        }
    }

    [Fact]
    public async Task GetAsync_ReflectsWebhookPastDueStatus()
    {
        var (db, service, tenantId) = CreateHarness();
        using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Past Due Co",
                Subdomain = "pastdue",
                Tier = SubscriptionTier.Professional,
                SubscriptionStatus = "past_due",
                EnabledFeatures = "ai,usage-tracking"
            });
            await db.SaveChangesAsync();

            var view = await service.GetAsync(tenantId);

            Assert.NotNull(view);
            Assert.True(view!.IsPastDue);
            Assert.Equal("past_due", view.Tenant.SubscriptionStatus);
        }
    }
}