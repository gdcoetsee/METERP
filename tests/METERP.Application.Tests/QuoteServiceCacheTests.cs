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
/// Verifies QuoteService list caching via ITenantCacheService — stale reads until invalidation.
/// </summary>
public class QuoteServiceCacheTests
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

    private static async Task<(Guid CustomerId, Guid QuoteId)> SeedQuoteAsync(AppDbContext db, Guid tenantId, string notes = "seeded")
    {
        var customer = new Customer { TenantId = tenantId, Name = "Cache Test Co" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-CACHE-001",
            QuoteDate = DateTime.UtcNow,
            Notes = notes,
            TaxRate = 0.15m
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        return (customer.Id, quote.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedQuoteAsync(db, tenantId, "original");
            var service = new QuoteService(db, cache: cache);

            var first = await service.GetAllAsync();
            Assert.Single(first);
            Assert.Equal("original", first[0].Notes);

            var quote = await db.Set<Quote>().FirstAsync();
            quote.Notes = "mutated-in-db";
            await db.SaveChangesAsync();

            var cached = await service.GetAllAsync();
            Assert.Equal("original", cached[0].Notes);

            cache.InvalidateCategory("quotes");

            var refreshed = await service.GetAllAsync();
            Assert.Equal("mutated-in-db", refreshed[0].Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesQuoteListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (customerId, _) = await SeedQuoteAsync(db, tenantId);
            var service = new QuoteService(db, cache: cache);

            var warm = await service.GetAllAsync();
            Assert.Single(warm);

            await service.CreateAsync(new Quote
            {
                TenantId = tenantId,
                CustomerId = customerId,
                QuoteNumber = "Q-CACHE-002",
                QuoteDate = DateTime.UtcNow,
                TaxRate = 0.15m
            });

            var afterCreate = await service.GetAllAsync();
            Assert.Equal(2, afterCreate.Count);
            Assert.Contains(afterCreate, q => q.QuoteNumber == "Q-CACHE-002");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedQuoteAsync(db, tenantId, "alpha notes");
            var customer = new Customer { TenantId = tenantId, Name = "Beta Mining Ltd" };
            db.Set<Customer>().Add(customer);
            db.Set<Quote>().Add(new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-BETA-SEARCH",
                QuoteDate = DateTime.UtcNow,
                Notes = "beta-only",
                TaxRate = 0.15m
            });
            await db.SaveChangesAsync();

            var service = new QuoteService(db, cache: cache);
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Quote>().FirstAsync(q => q.QuoteNumber == "Q-BETA-SEARCH");
            beta.Notes = "beta-mutated";
            await db.SaveChangesAsync();

            var searchResult = await service.GetAllAsync(search: "Beta Mining", pageSize: 50);
            Assert.Single(searchResult);
            Assert.Equal("beta-mutated", searchResult[0].Notes);
        }
    }
}