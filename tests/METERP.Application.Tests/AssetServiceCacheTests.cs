using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AssetServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, AssetService Service) CreateHarness(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
        var provider = services.BuildServiceProvider();
        var cache = new TenantDistributedCacheService(
            provider.GetRequiredService<IDistributedCache>(),
            tenantProvider.Object,
            provider.GetRequiredService<IOptions<CacheOptions>>());

        return (db, cache, new AssetService(db, cache));
    }

    private static async Task<Guid> SeedCustomerAsync(AppDbContext db, Guid tenantId)
    {
        var customer = new Customer { TenantId = tenantId, Name = "Asset Owner Co" };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            var customerId = await SeedCustomerAsync(db, tenantId);
            db.Set<Asset>().Add(new Asset { TenantId = tenantId, CustomerId = customerId, AssetNumber = "AST-001", Name = "Original Panel" });
            await db.SaveChangesAsync();

            Assert.Equal("Original Panel", (await service.GetAllAsync())[0].Name);
            (await db.Set<Asset>().FirstAsync()).Name = "Mutated Panel";
            await db.SaveChangesAsync();
            Assert.Equal("Original Panel", (await service.GetAllAsync())[0].Name);

            cache.InvalidateCategory("assets");
            Assert.Equal("Mutated Panel", (await service.GetAllAsync())[0].Name);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesAssetListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var customerId = await SeedCustomerAsync(db, tenantId);
            await service.CreateAsync(new Asset { TenantId = tenantId, CustomerId = customerId, Name = "Transformer A" });
            Assert.Single(await service.GetAllAsync());
            await service.CreateAsync(new Asset { TenantId = tenantId, CustomerId = customerId, Name = "Transformer B" });
            Assert.Equal(2, (await service.GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var customerId = await SeedCustomerAsync(db, tenantId);
            db.Set<Asset>().Add(new Asset { TenantId = tenantId, CustomerId = customerId, AssetNumber = "AST-C", Name = "Cached Asset" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            (await db.Set<Asset>().FirstAsync()).Name = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Cached Asset", (await service.GetAllAsync(search: "   ", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_InvalidatesAssetListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var customerId = await SeedCustomerAsync(db, tenantId);
            var id = await service.CreateAsync(new Asset { TenantId = tenantId, CustomerId = customerId, Name = "Switchgear", Status = AssetStatus.Operational });
            await service.GetAllAsync();
            await service.UpdateStatusAsync(id, AssetStatus.UnderMaintenance);
            Assert.Equal(AssetStatus.UnderMaintenance, (await service.GetAllAsync())[0].Status);
        }
    }
}