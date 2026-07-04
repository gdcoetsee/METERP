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

public class PurchaseOrderServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache) CreateHarness(Guid tenantId)
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

        return (db, cache);
    }

    private static async Task<(Guid SupplierId, Guid PoId)> SeedPurchaseOrderAsync(
        AppDbContext db, Guid tenantId, string notes = "seeded")
    {
        var supplier = new Supplier { TenantId = tenantId, Name = "PO Cache Supplier" };
        db.Set<Supplier>().Add(supplier);

        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = supplier.Id,
            PoNumber = "PO-CACHE-001",
            PoDate = DateTime.UtcNow,
            Notes = notes,
            TaxRate = 0.15m
        };
        db.Set<PurchaseOrder>().Add(po);
        await db.SaveChangesAsync();

        return (supplier.Id, po.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedPurchaseOrderAsync(db, tenantId, "original");
            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);

            Assert.Equal("original", (await service.GetAllAsync())[0].Notes);

            var po = await db.Set<PurchaseOrder>().FirstAsync();
            po.Notes = "mutated-in-db";
            await db.SaveChangesAsync();

            Assert.Equal("original", (await service.GetAllAsync())[0].Notes);

            cache.InvalidateCategory(TenantCacheCategories.PurchaseOrders);

            Assert.Equal("mutated-in-db", (await service.GetAllAsync())[0].Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesPurchaseOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (supplierId, _) = await SeedPurchaseOrderAsync(db, tenantId);
            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);

            Assert.Single(await service.GetAllAsync());

            await service.CreateAsync(new PurchaseOrder
            {
                TenantId = tenantId,
                SupplierId = supplierId,
                PoNumber = "PO-CACHE-002",
                PoDate = DateTime.UtcNow,
                TaxRate = 0.15m
            });

            Assert.Equal(2, (await service.GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedPurchaseOrderAsync(db, tenantId, "alpha");
            var supplier = new Supplier { TenantId = tenantId, Name = "Beta Supply Ltd" };
            db.Set<Supplier>().Add(supplier);
            db.Set<PurchaseOrder>().Add(new PurchaseOrder
            {
                TenantId = tenantId,
                SupplierId = supplier.Id,
                PoNumber = "PO-BETA",
                Notes = "beta-only",
                TaxRate = 0.15m
            });
            await db.SaveChangesAsync();

            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<PurchaseOrder>().FirstAsync(p => p.PoNumber == "PO-BETA");
            beta.Notes = "beta-mutated";
            await db.SaveChangesAsync();

            Assert.Equal("beta-mutated", (await service.GetAllAsync(search: "Beta Supply", pageSize: 50))[0].Notes);
        }
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesPurchaseOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedPurchaseOrderAsync(db, tenantId, "original");
            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);

            await service.GetAllAsync();
            var po = await db.Set<PurchaseOrder>().FirstAsync();
            po.Notes = "updated-via-service";
            await service.UpdateAsync(po);

            Assert.Equal("updated-via-service", (await service.GetAllAsync())[0].Notes);
        }
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesPurchaseOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, poId) = await SeedPurchaseOrderAsync(db, tenantId);
            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);

            Assert.Single(await service.GetAllAsync());
            await service.DeleteAsync(poId);

            Assert.Empty(await service.GetAllAsync());
        }
    }

    [Fact]
    public async Task AddLineAsync_InvalidatesPurchaseOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, poId) = await SeedPurchaseOrderAsync(db, tenantId);
            var service = new PurchaseOrderService(db, new InventoryService(db), cache: cache);

            Assert.Equal(0m, (await service.GetAllAsync())[0].Total);

            await service.AddLineAsync(new PurchaseOrderLine
            {
                TenantId = tenantId,
                PurchaseOrderId = poId,
                Description = "Cable",
                Quantity = 10m,
                UnitPrice = 50m
            });

            Assert.Equal(575m, (await service.GetAllAsync())[0].Total);
        }
    }
}