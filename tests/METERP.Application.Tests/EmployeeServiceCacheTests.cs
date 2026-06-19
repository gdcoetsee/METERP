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

public class EmployeeServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, EmployeeService Service) CreateHarness(Guid tenantId)
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

        return (db, cache, new EmployeeService(db, cache));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Employee>().Add(new Employee { TenantId = tenantId, EmployeeNumber = "E-001", FirstName = "Alex", LastName = "Original" });
            await db.SaveChangesAsync();

            Assert.Equal("Original", (await service.GetAllAsync())[0].LastName);
            (await db.Set<Employee>().FirstAsync()).LastName = "Mutated";
            await db.SaveChangesAsync();
            Assert.Equal("Original", (await service.GetAllAsync())[0].LastName);

            cache.InvalidateCategory("employees");
            Assert.Equal("Mutated", (await service.GetAllAsync())[0].LastName);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesEmployeeListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAsync(new Employee { TenantId = tenantId, EmployeeNumber = "E-001", FirstName = "A", LastName = "One" });
            Assert.Single(await service.GetAllAsync());
            await service.CreateAsync(new Employee { TenantId = tenantId, EmployeeNumber = "E-002", FirstName = "B", LastName = "Two" });
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
            db.Set<Employee>().Add(new Employee { TenantId = tenantId, EmployeeNumber = "E-C", FirstName = "Cached", LastName = "Worker" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            (await db.Set<Employee>().FirstAsync()).LastName = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Worker", (await service.GetAllAsync(search: "   ", pageSize: 50))[0].LastName);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Employee>().Add(new Employee { TenantId = tenantId, EmployeeNumber = "E-A", FirstName = "Alpha", LastName = "Tech" });
            db.Set<Employee>().Add(new Employee { TenantId = tenantId, EmployeeNumber = "E-B", FirstName = "Beta", LastName = "Crew" });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Employee>().FirstAsync(e => e.LastName == "Crew");
            beta.LastName = "Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Mutated", (await service.GetAllAsync(search: "Beta", pageSize: 50))[0].LastName);
        }
    }
}