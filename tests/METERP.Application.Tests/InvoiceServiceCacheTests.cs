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
/// Verifies InvoiceService list caching via ITenantCacheService.
/// </summary>
public class InvoiceServiceCacheTests
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

    private static async Task<(Guid CustomerId, Guid InvoiceId)> SeedInvoiceAsync(AppDbContext db, Guid tenantId, string notes = "seeded")
    {
        var customer = new Customer { TenantId = tenantId, Name = "Invoice Cache Co" };
        db.Set<Customer>().Add(customer);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            InvoiceNumber = "INV-CACHE-001",
            InvoiceDate = DateTime.UtcNow,
            Notes = notes,
            TaxRate = 0.15m
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        return (customer.Id, invoice.Id);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedInvoiceAsync(db, tenantId, "original");
            var service = new InvoiceService(db, cache: cache);

            var first = await service.GetAllAsync();
            Assert.Single(first);
            Assert.Equal("original", first[0].Notes);

            var invoice = await db.Set<Invoice>().FirstAsync();
            invoice.Notes = "mutated-in-db";
            await db.SaveChangesAsync();

            var cached = await service.GetAllAsync();
            Assert.Equal("original", cached[0].Notes);

            cache.InvalidateCategory(TenantCacheCategories.Invoices);

            var refreshed = await service.GetAllAsync();
            Assert.Equal("mutated-in-db", refreshed[0].Notes);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (customerId, _) = await SeedInvoiceAsync(db, tenantId);
            var service = new InvoiceService(db, cache: cache);

            Assert.Single(await service.GetAllAsync());

            await service.CreateAsync(new Invoice
            {
                TenantId = tenantId,
                CustomerId = customerId,
                InvoiceNumber = "INV-CACHE-002",
                InvoiceDate = DateTime.UtcNow,
                TaxRate = 0.15m
            });

            var afterCreate = await service.GetAllAsync();
            Assert.Equal(2, afterCreate.Count);
            Assert.Contains(afterCreate, i => i.InvoiceNumber == "INV-CACHE-002");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedInvoiceAsync(db, tenantId, "alpha notes");
            var customer = new Customer { TenantId = tenantId, Name = "Beta Billing Ltd" };
            db.Set<Customer>().Add(customer);
            db.Set<Invoice>().Add(new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-BETA-SEARCH",
                InvoiceDate = DateTime.UtcNow,
                Notes = "beta-only",
                TaxRate = 0.15m
            });
            await db.SaveChangesAsync();

            var service = new InvoiceService(db, cache: cache);
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Invoice>().FirstAsync(i => i.InvoiceNumber == "INV-BETA-SEARCH");
            beta.Notes = "beta-mutated";
            await db.SaveChangesAsync();

            var searchResult = await service.GetAllAsync(search: "Beta Billing", pageSize: 50);
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
            await SeedInvoiceAsync(db, tenantId, "cached-notes");
            var service = new InvoiceService(db, cache: cache);

            await service.GetAllAsync(pageSize: 50);

            var invoice = await db.Set<Invoice>().FirstAsync();
            invoice.Notes = "db-mutated";
            await db.SaveChangesAsync();

            var whitespace = await service.GetAllAsync(search: "   ", pageSize: 50);
            Assert.Equal("cached-notes", whitespace[0].Notes);
        }
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            await SeedInvoiceAsync(db, tenantId, "original");
            var service = new InvoiceService(db, cache: cache);

            await service.GetAllAsync();
            var invoice = await db.Set<Invoice>().FirstAsync();
            invoice.Notes = "updated-via-service";
            await service.UpdateAsync(invoice);

            var refreshed = await service.GetAllAsync();
            Assert.Equal("updated-via-service", refreshed[0].Notes);
        }
    }

    [Fact]
    public async Task DeleteAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, invoiceId) = await SeedInvoiceAsync(db, tenantId);
            var service = new InvoiceService(db, cache: cache);

            Assert.Single(await service.GetAllAsync());
            await service.DeleteAsync(invoiceId);

            var afterDelete = await service.GetAllAsync();
            Assert.Empty(afterDelete);
        }
    }

    [Fact]
    public async Task AddLineAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, invoiceId) = await SeedInvoiceAsync(db, tenantId);
            var service = new InvoiceService(db, cache: cache);

            var warm = await service.GetAllAsync();
            Assert.Empty(warm[0].Lines);

            await service.AddLineAsync(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = invoiceId,
                Description = "New cached line",
                Quantity = 2m,
                UnitPrice = 100m
            });

            var refreshed = await service.GetAllAsync();
            Assert.Single(refreshed[0].Lines);
            Assert.Equal("New cached line", refreshed[0].Lines.First().Description);
        }
    }

    [Fact]
    public async Task UpdateLineAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, invoiceId) = await SeedInvoiceAsync(db, tenantId);
            var line = new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = invoiceId,
                Description = "original-line",
                Quantity = 1m,
                UnitPrice = 50m
            };
            db.Set<InvoiceLine>().Add(line);
            await db.SaveChangesAsync();

            var service = new InvoiceService(db, cache: cache);
            await service.GetAllAsync();

            line.Description = "updated-line";
            await service.UpdateLineAsync(line);

            var refreshed = await service.GetAllAsync();
            Assert.Equal("updated-line", refreshed[0].Lines.First().Description);
        }
    }

    [Fact]
    public async Task GetAllAsync_DifferentPageSizes_UseSeparateCacheEntries()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Invoice Paging Co" };
            db.Set<Customer>().Add(customer);
            for (var i = 1; i <= 3; i++)
            {
                db.Set<Invoice>().Add(new Invoice
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    InvoiceNumber = $"INV-PAGE-{i:000}",
                    InvoiceDate = DateTime.UtcNow.AddDays(-i),
                    Notes = $"page-seed-{i}",
                    TaxRate = 0.15m
                });
            }
            await db.SaveChangesAsync();

            var service = new InvoiceService(db, cache: cache);
            var page1 = await service.GetAllAsync(page: 1, pageSize: 2);
            Assert.Equal(2, page1.Count);

            var oldest = await db.Set<Invoice>().OrderBy(i => i.InvoiceDate).FirstAsync();
            oldest.Notes = "oldest-mutated";
            await db.SaveChangesAsync();

            var page1Cached = await service.GetAllAsync(page: 1, pageSize: 2);
            Assert.Equal("page-seed-1", page1Cached[0].Notes);

            var page2Fresh = await service.GetAllAsync(page: 2, pageSize: 2);
            Assert.Single(page2Fresh);
            Assert.Equal("oldest-mutated", page2Fresh[0].Notes);
        }
    }

    [Fact]
    public async Task DeleteLineAsync_InvalidatesInvoiceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache) = CreateHarness(tenantId);
        using (db)
        {
            var (_, invoiceId) = await SeedInvoiceAsync(db, tenantId);
            var line = new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = invoiceId,
                Description = "to-delete",
                Quantity = 1m,
                UnitPrice = 25m
            };
            db.Set<InvoiceLine>().Add(line);
            await db.SaveChangesAsync();

            var service = new InvoiceService(db, cache: cache);
            Assert.Single((await service.GetAllAsync())[0].Lines);

            await service.DeleteLineAsync(line.Id);

            var refreshed = await service.GetAllAsync();
            Assert.Empty(refreshed[0].Lines);
        }
    }
}