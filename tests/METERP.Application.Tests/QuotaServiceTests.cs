using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class QuotaServiceTests
{
    private IServiceScopeFactory CreateScopeFactory(out AppDbContext seedContext)
    {
        var dbName = Guid.NewGuid().ToString();

        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(Guid.NewGuid());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(s => s.UserName).Returns("test-user");

        var services = new ServiceCollection();
        services.AddScoped(_ => tenantProviderMock.Object);
        services.AddScoped(_ => currentUserMock.Object);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateScope();
        seedContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return scopeFactory;
    }

    private static Tenant CreateTenant(
        Guid id,
        SubscriptionTier tier = SubscriptionTier.Starter,
        int periodQuotes = 0,
        DateTime? periodStart = null,
        int? maxQuotes = null)
    {
        return new Tenant
        {
            Id = id,
            TenantId = id,
            Name = "Test Tenant",
            Subdomain = $"t-{id:N}".Substring(0, 12),
            Tier = tier,
            UsagePeriodStartUtc = periodStart ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodQuotesCreated = periodQuotes,
            MaxQuotesPerMonth = maxQuotes
        };
    }

    [Fact]
    public async Task EnsureAllowedAsync_Allows_WhenUnderLimit()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(CreateTenant(tenantId, periodQuotes: 5));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        await service.EnsureAllowedAsync(tenantId, QuotaType.Quote);
    }

    [Fact]
    public async Task EnsureAllowedAsync_Throws_WhenAtLimit()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(CreateTenant(tenantId, periodQuotes: 20));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        var ex = await Assert.ThrowsAsync<QuotaExceededException>(
            () => service.EnsureAllowedAsync(tenantId, QuotaType.Quote));

        Assert.Equal(QuotaType.Quote, ex.QuotaType);
        Assert.Equal(20, ex.Limit);
        Assert.Equal(20, ex.Used);
    }

    [Fact]
    public async Task EnsureAllowedAsync_Allows_EnterpriseUnlimited()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(CreateTenant(tenantId, tier: SubscriptionTier.Enterprise, periodQuotes: 9999));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        await service.EnsureAllowedAsync(tenantId, QuotaType.Quote);
    }

    [Fact]
    public async Task EnsureAllowedAsync_ResetsPeriodCounters_WhenNewMonth()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var tenantId = Guid.NewGuid();
        var oldPeriod = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Tenants.Add(CreateTenant(tenantId, periodQuotes: 20, periodStart: oldPeriod));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        await service.EnsureAllowedAsync(tenantId, QuotaType.Quote);

        using var verifyScope = scopeFactory.CreateScope();
        var updated = await verifyScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == tenantId);
        Assert.Equal(0, updated.PeriodQuotesCreated);
        var currentPeriod = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(updated.UsagePeriodStartUtc >= currentPeriod);
    }

    [Fact]
    public async Task EnsureAllowedAsync_RespectsTenantOverride()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(CreateTenant(tenantId, periodQuotes: 3, maxQuotes: 3));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        await Assert.ThrowsAsync<QuotaExceededException>(
            () => service.EnsureAllowedAsync(tenantId, QuotaType.Quote));
    }

    [Fact]
    public async Task EnsureAllowedAsync_IsolatesTenants()
    {
        var scopeFactory = CreateScopeFactory(out var db);
        var blockedId = Guid.NewGuid();
        var allowedId = Guid.NewGuid();
        db.Tenants.Add(CreateTenant(blockedId, periodQuotes: 20));
        db.Tenants.Add(CreateTenant(allowedId, periodQuotes: 0));
        await db.SaveChangesAsync();

        var service = new QuotaService(scopeFactory);
        await Assert.ThrowsAsync<QuotaExceededException>(
            () => service.EnsureAllowedAsync(blockedId, QuotaType.Quote));
        await service.EnsureAllowedAsync(allowedId, QuotaType.Quote);
    }
}