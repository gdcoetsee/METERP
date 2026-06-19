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

public class SupplierServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, SupplierService Service) CreateHarness(Guid tenantId)
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

        return (db, cache, new SupplierService(db, cache));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Supplier>().Add(new Supplier { TenantId = tenantId, Name = "Original Supply" });
            await db.SaveChangesAsync();

            Assert.Equal("Original Supply", (await service.GetAllAsync())[0].Name);
            (await db.Set<Supplier>().FirstAsync()).Name = "Mutated Supply";
            await db.SaveChangesAsync();
            Assert.Equal("Original Supply", (await service.GetAllAsync())[0].Name);

            cache.InvalidateCategory("suppliers");
            Assert.Equal("Mutated Supply", (await service.GetAllAsync())[0].Name);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesSupplierListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAsync(new Supplier { TenantId = tenantId, Name = "Cable Co" });
            Assert.Single(await service.GetAllAsync());
            await service.CreateAsync(new Supplier { TenantId = tenantId, Name = "Panel Co" });
            Assert.Equal(2, (await service.GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesSupplierListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAsync(new Supplier { TenantId = tenantId, Name = "Before" });
            await service.GetAllAsync();
            var supplier = await db.Set<Supplier>().FirstAsync();
            supplier.Name = "After";
            await service.UpdateAsync(supplier);
            Assert.Equal("After", (await service.GetAllAsync())[0].Name);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Supplier>().Add(new Supplier { TenantId = tenantId, Name = "Cached Supply" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            (await db.Set<Supplier>().FirstAsync()).Name = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Cached Supply", (await service.GetAllAsync(search: "   ", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesSupplierListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var id = await service.CreateAsync(new Supplier { TenantId = tenantId, Name = "Remove Me" });
            Assert.Single(await service.GetAllAsync());
            await service.DeleteAsync(id);
            Assert.Empty(await service.GetAllAsync());
        }
    }
}