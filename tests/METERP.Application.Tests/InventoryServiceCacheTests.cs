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

public class InventoryServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, InventoryService Service) CreateHarness(Guid tenantId)
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

        return (db, cache, new InventoryService(db, cache));
    }

    [Fact]
    public async Task GetAllItemsAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantId, Sku = "SKU-1", Name = "Original Cable" });
            await db.SaveChangesAsync();

            Assert.Equal("Original Cable", (await service.GetAllItemsAsync())[0].Name);
            (await db.Set<InventoryItem>().FirstAsync()).Name = "Mutated Cable";
            await db.SaveChangesAsync();
            Assert.Equal("Original Cable", (await service.GetAllItemsAsync())[0].Name);

            cache.InvalidateCategory("inventory");
            Assert.Equal("Mutated Cable", (await service.GetAllItemsAsync())[0].Name);
        }
    }

    [Fact]
    public async Task CreateItemAsync_InvalidatesInventoryListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateItemAsync(new InventoryItem { TenantId = tenantId, Name = "Breaker A" });
            Assert.Single(await service.GetAllItemsAsync());
            await service.CreateItemAsync(new InventoryItem { TenantId = tenantId, Name = "Breaker B" });
            Assert.Equal(2, (await service.GetAllItemsAsync()).Count);
        }
    }

    [Fact]
    public async Task GetAllItemsAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantId, Sku = "A-1", Name = "Alpha Panel" });
            db.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantId, Sku = "B-1", Name = "Beta Cable" });
            await db.SaveChangesAsync();
            await service.GetAllItemsAsync(pageSize: 50);

            var beta = await db.Set<InventoryItem>().FirstAsync(i => i.Name == "Beta Cable");
            beta.Name = "Beta Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Beta Mutated", (await service.GetAllItemsAsync(search: "Beta", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task GetAllItemsAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantId, Sku = "W-1", Name = "Cached Item" });
            await db.SaveChangesAsync();
            await service.GetAllItemsAsync(pageSize: 50);

            (await db.Set<InventoryItem>().FirstAsync()).Name = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Cached Item", (await service.GetAllItemsAsync(search: "   ", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task RecordStockTransactionAsync_InvalidatesInventoryListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var id = await service.CreateItemAsync(new InventoryItem
            {
                TenantId = tenantId,
                Sku = "LOW-1",
                Name = "Stock Part",
                QuantityOnHand = 10m,
                ReorderLevel = 5m
            });
            await service.GetAllItemsAsync(pageSize: 50);
            await service.RecordStockTransactionAsync(id, -8m, StockTransactionType.Adjustment);
            Assert.Equal(2m, (await service.GetAllItemsAsync(pageSize: 50))[0].QuantityOnHand);
        }
    }

    [Fact]
    public async Task GetAllItemsAsync_LowStockFilter_UsesSeparateCacheEntries()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<InventoryItem>().Add(new InventoryItem
            {
                TenantId = tenantId,
                Sku = "FULL-1",
                Name = "Well Stocked",
                QuantityOnHand = 20m,
                ReorderLevel = 5m
            });
            db.Set<InventoryItem>().Add(new InventoryItem
            {
                TenantId = tenantId,
                Sku = "LOW-1",
                Name = "Low Stock",
                QuantityOnHand = 2m,
                ReorderLevel = 5m
            });
            await db.SaveChangesAsync();

            Assert.Equal(2, (await service.GetAllItemsAsync(pageSize: 50)).Count);
            Assert.Single(await service.GetAllItemsAsync(lowStockOnly: true, pageSize: 50));

            (await db.Set<InventoryItem>().FirstAsync(i => i.Name == "Low Stock")).QuantityOnHand = 20m;
            await db.SaveChangesAsync();

            Assert.Equal(2, (await service.GetAllItemsAsync(pageSize: 50)).Count);
            Assert.Single(await service.GetAllItemsAsync(lowStockOnly: true, pageSize: 50));

            cache.InvalidateCategory("inventory");
            Assert.Empty(await service.GetAllItemsAsync(lowStockOnly: true, pageSize: 50));
        }
    }
}