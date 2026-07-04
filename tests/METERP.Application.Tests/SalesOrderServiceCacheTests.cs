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

public class SalesOrderServiceCacheTests
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

    private static async Task<(Guid CustomerId, Guid QuoteId, Guid SoId)> SeedSalesOrderAsync(
        AppDbContext db, Guid tenantId, string notes = "seeded")
    {
        var customer = new Customer { TenantId = tenantId, Name = "SO Cache Co" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-SO-CACHE",
            QuoteDate = DateTime.UtcNow,
            TaxRate = 0.15m
        };
        db.Set<Quote>().Add(quote);

        var so = new SalesOrder
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            SoNumber = "SO-CACHE-001",
            SoDate = DateTime.UtcNow,
            Notes = notes,
            TaxRate = 0.15m
        };
        db.Set<SalesOrder>().Add(so);
        await db.SaveChangesAsync();

        return (customer.Id, quote.Id, so.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedSalesOrderAsync(db, tenantId, "original");
            var service = new SalesOrderService(db, new JobService(db), cache: cache);

            Assert.Equal("original", (await service.GetAllAsync())[0].Notes);

            var so = await db.Set<SalesOrder>().FirstAsync();
            so.Notes = "mutated-in-db";
            await db.SaveChangesAsync();

            Assert.Equal("original", (await service.GetAllAsync())[0].Notes);

            cache.InvalidateCategory(TenantCacheCategories.SalesOrders);

            Assert.Equal("mutated-in-db", (await service.GetAllAsync())[0].Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesSalesOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (customerId, quoteId, _) = await SeedSalesOrderAsync(db, tenantId);
            var service = new SalesOrderService(db, new JobService(db), cache: cache);

            Assert.Single(await service.GetAllAsync());

            await service.CreateAsync(new SalesOrder
            {
                TenantId = tenantId,
                CustomerId = customerId,
                QuoteId = quoteId,
                SoNumber = "SO-CACHE-002",
                SoDate = DateTime.UtcNow,
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
            await SeedSalesOrderAsync(db, tenantId, "alpha");
            var customer = new Customer { TenantId = tenantId, Name = "Beta Sales Ltd" };
            db.Set<Customer>().Add(customer);
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-BETA",
                TaxRate = 0.15m
            };
            db.Set<Quote>().Add(quote);
            db.Set<SalesOrder>().Add(new SalesOrder
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteId = quote.Id,
                SoNumber = "SO-BETA",
                Notes = "beta-only",
                TaxRate = 0.15m
            });
            await db.SaveChangesAsync();

            var service = new SalesOrderService(db, new JobService(db), cache: cache);
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<SalesOrder>().FirstAsync(s => s.SoNumber == "SO-BETA");
            beta.Notes = "beta-mutated";
            await db.SaveChangesAsync();

            Assert.Equal("beta-mutated", (await service.GetAllAsync(search: "Beta Sales", pageSize: 50))[0].Notes);
        }
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesSalesOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedSalesOrderAsync(db, tenantId, "original");
            var service = new SalesOrderService(db, new JobService(db), cache: cache);

            await service.GetAllAsync();
            var so = await db.Set<SalesOrder>().FirstAsync();
            so.Notes = "updated-via-service";
            await service.UpdateAsync(so);

            Assert.Equal("updated-via-service", (await service.GetAllAsync())[0].Notes);
        }
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesSalesOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, _, soId) = await SeedSalesOrderAsync(db, tenantId);
            var service = new SalesOrderService(db, new JobService(db), cache: cache);

            Assert.Single(await service.GetAllAsync());
            await service.DeleteAsync(soId);

            Assert.Empty(await service.GetAllAsync());
        }
    }

    [Fact]
    public async Task AddLineAsync_InvalidatesSalesOrderListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, _, soId) = await SeedSalesOrderAsync(db, tenantId);
            var service = new SalesOrderService(db, new JobService(db), cache: cache);

            Assert.Equal(0m, (await service.GetAllAsync())[0].Total);

            await service.AddLineAsync(new SalesOrderLine
            {
                TenantId = tenantId,
                SalesOrderId = soId,
                Description = "Panel",
                Quantity = 1m,
                UnitPrice = 1000m
            });

            Assert.Equal(1150m, (await service.GetAllAsync())[0].Total);
        }
    }

    [Fact]
    public async Task ConvertToJobAsync_InvalidatesSalesOrderJobAndInvoiceListCaches()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (customerId, quoteId, soId) = await SeedSalesOrderAsync(db, tenantId);
            var so = await db.Set<SalesOrder>().FirstAsync(s => s.Id == soId);
            so.Status = SalesOrderStatus.Confirmed;
            so.Total = 5000m;
            so.Subtotal = 5000m;
            await db.SaveChangesAsync();

            var jobService = new JobService(db, cache: cache);
            var invoiceService = new InvoiceService(db, cache: cache);
            var service = new SalesOrderService(db, jobService, cache: cache);

            Assert.Equal(SalesOrderStatus.Confirmed, (await service.GetAllAsync())[0].Status);
            Assert.Empty(await jobService.GetAllAsync());
            Assert.Empty(await invoiceService.GetAllAsync());
            Assert.Empty(await invoiceService.GetAllAsync());

            await service.ConvertToJobAsync(soId);

            var job = (await jobService.GetAllAsync()).Single();
            db.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantId,
                CustomerId = customerId,
                JobId = job.Id,
                InvoiceNumber = "INV-SO-CONVERT",
                TaxRate = 0.15m,
                Notes = "Created after convert cache bust"
            });
            await db.SaveChangesAsync();

            Assert.Equal(SalesOrderStatus.InProgress, (await service.GetAllAsync())[0].Status);
            Assert.Single(await jobService.GetAllAsync());
            Assert.Equal("Created after convert cache bust", (await invoiceService.GetAllAsync())[0].Notes);
        }
    }
}