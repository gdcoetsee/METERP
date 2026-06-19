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

/// <summary>
/// Verifies JobService list caching via ITenantCacheService.
/// </summary>
public class JobServiceCacheTests
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

    private static async Task<Guid> SeedJobAsync(AppDbContext db, Guid tenantId, string notes = "seeded")
    {
        var customer = new Customer { TenantId = tenantId, Name = "Job Cache Co" };
        db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-CACHE-001",
            Title = "Panel upgrade",
            Notes = notes,
            QuotedTotal = 5000m
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        return job.Id;
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedJobAsync(db, tenantId, "original");
            var service = new JobService(db, cache: cache);

            var first = await service.GetAllAsync();
            Assert.Single(first);
            Assert.Equal("original", first[0].Notes);

            var job = await db.Set<Job>().FirstAsync();
            job.Notes = "mutated-in-db";
            await db.SaveChangesAsync();

            var cached = await service.GetAllAsync();
            Assert.Equal("original", cached[0].Notes);

            cache.InvalidateCategory("jobs");

            var refreshed = await service.GetAllAsync();
            Assert.Equal("mutated-in-db", refreshed[0].Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesJobListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var customer = await db.Set<Customer>().FirstOrDefaultAsync();
            if (customer == null)
            {
                customer = new Customer { TenantId = tenantId, Name = "Job Cache Co" };
                db.Set<Customer>().Add(customer);
                await db.SaveChangesAsync();
            }

            await SeedJobAsync(db, tenantId);
            var service = new JobService(db, cache: cache);

            Assert.Single(await service.GetAllAsync());

            await service.CreateAsync(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobNumber = "J-CACHE-002",
                Title = "Second job",
                QuotedTotal = 1200m
            });

            var afterCreate = await service.GetAllAsync();
            Assert.Equal(2, afterCreate.Count);
            Assert.Contains(afterCreate, j => j.JobNumber == "J-CACHE-002");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Alpha Customer" };
            db.Set<Customer>().Add(customer);
            db.Set<Job>().Add(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobNumber = "J-ALPHA-001",
                Title = "Alpha install",
                Notes = "alpha-only",
                QuotedTotal = 1000m
            });
            db.Set<Job>().Add(new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobNumber = "J-BETA-SEARCH",
                Title = "Beta retrofit",
                Notes = "beta-only",
                QuotedTotal = 2000m
            });
            await db.SaveChangesAsync();

            var service = new JobService(db, cache: cache);
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Job>().FirstAsync(j => j.JobNumber == "J-BETA-SEARCH");
            beta.Notes = "beta-mutated";
            await db.SaveChangesAsync();

            var searchResult = await service.GetAllAsync(search: "Beta retrofit", pageSize: 50);
            Assert.Single(searchResult);
            Assert.Equal("beta-mutated", searchResult[0].Notes);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedJobAsync(db, tenantId, "cached-job");
            var service = new JobService(db, cache: cache);

            await service.GetAllAsync(pageSize: 50);

            var job = await db.Set<Job>().FirstAsync();
            job.Notes = "db-mutated";
            await db.SaveChangesAsync();

            var whitespace = await service.GetAllAsync(search: "\t", pageSize: 50);
            Assert.Equal("cached-job", whitespace[0].Notes);
        }
    }
}