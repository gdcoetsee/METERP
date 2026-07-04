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

public class CustomerServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, CustomerService Service) CreateHarness(Guid tenantId)
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

        return (db, cache, new CustomerService(db, cache));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Customer>().Add(new Customer { TenantId = tenantId, Name = "Original Co" });
            await db.SaveChangesAsync();

            Assert.Equal("Original Co", (await service.GetAllAsync())[0].Name);
            (await db.Set<Customer>().FirstAsync()).Name = "Mutated Co";
            await db.SaveChangesAsync();
            Assert.Equal("Original Co", (await service.GetAllAsync())[0].Name);

            cache.InvalidateCategory(TenantCacheCategories.Customers);
            Assert.Equal("Mutated Co", (await service.GetAllAsync())[0].Name);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesCustomerListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAsync(new Customer { TenantId = tenantId, Name = "First" });
            Assert.Single(await service.GetAllAsync());
            await service.CreateAsync(new Customer { TenantId = tenantId, Name = "Second" });
            Assert.Equal(2, (await service.GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Customer>().Add(new Customer { TenantId = tenantId, Name = "Alpha Ltd" });
            db.Set<Customer>().Add(new Customer { TenantId = tenantId, Name = "Beta Mining" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Customer>().FirstAsync(c => c.Name == "Beta Mining");
            beta.Name = "Beta Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Beta Mutated", (await service.GetAllAsync(search: "Beta", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Customer>().Add(new Customer { TenantId = tenantId, Name = "Cached Co" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            (await db.Set<Customer>().FirstAsync()).Name = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Cached Co", (await service.GetAllAsync(search: "   ", pageSize: 50))[0].Name);
        }
    }

    [Fact]
    public async Task AddContactAsync_InvalidatesCustomerListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var customerId = await service.CreateAsync(new Customer { TenantId = tenantId, Name = "Contact Co" });
            Assert.Empty((await service.GetAllAsync())[0].Contacts);

            await service.AddContactAsync(new Contact
            {
                TenantId = tenantId,
                CustomerId = customerId,
                FirstName = "Sam",
                LastName = "Tech",
                Email = "sam@contact.test"
            });

            Assert.Single((await service.GetAllAsync())[0].Contacts);
        }
    }
}