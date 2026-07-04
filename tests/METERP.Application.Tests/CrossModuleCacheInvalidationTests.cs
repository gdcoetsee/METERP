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
/// Customer master-data mutations must refresh CRM/spine list caches that embed Customer navigation.
/// </summary>
public class CrossModuleCacheInvalidationTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public TenantDistributedCacheService Cache { get; }
        public CustomerService Customers { get; }
        public OpportunityService Opportunities { get; }
        public QuoteService Quotes { get; }
        public JobService Jobs { get; }

        public Harness(Guid tenantId)
        {
            TenantId = tenantId;
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(s => s.TenantId).Returns(tenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
            var provider = services.BuildServiceProvider();
            Cache = new TenantDistributedCacheService(
                provider.GetRequiredService<IDistributedCache>(),
                tenantProvider.Object,
                provider.GetRequiredService<IOptions<CacheOptions>>());

            Customers = new CustomerService(Db, Cache);
            Opportunities = new OpportunityService(Db, cache: Cache);
            Quotes = new QuoteService(Db, cache: Cache);
            Jobs = new JobService(Db, cache: Cache);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Customer> SeedCustomerWithSpineAsync(Harness harness, string customerName)
    {
        var customer = new Customer
        {
            TenantId = harness.TenantId,
            Name = customerName
        };
        harness.Db.Set<Customer>().Add(customer);

        harness.Db.Set<Opportunity>().Add(new Opportunity
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            Title = "Panel upgrade opp",
            Value = 12000m
        });

        harness.Db.Set<Quote>().Add(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-CROSS-001",
            TaxRate = 0.15m
        });

        harness.Db.Set<Job>().Add(new Job
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            JobNumber = "J-CROSS-001",
            Title = "Install job",
            QuotedTotal = 9000m
        });

        await harness.Db.SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesOpportunityListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Opportunities.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Opportunities.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesQuoteListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Quotes.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Quotes.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerUpdate_InvalidatesJobListCache_WithFreshCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Old Customer Co");

        Assert.Equal("Old Customer Co", (await harness.Jobs.GetAllAsync())[0].Customer!.Name);

        customer.Name = "Renamed Customer Co";
        await harness.Customers.UpdateAsync(customer);

        var refreshed = await harness.Jobs.GetAllAsync();
        Assert.Equal("Renamed Customer Co", refreshed[0].Customer!.Name);
    }

    [Fact]
    public async Task CustomerDelete_InvalidatesOpportunityListCache()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        var customer = await SeedCustomerWithSpineAsync(harness, "Delete Me Co");

        Assert.Equal("Panel upgrade opp", (await harness.Opportunities.GetAllAsync())[0].Title);

        await harness.Customers.DeleteAsync(customer.Id);

        var opp = await harness.Db.Set<Opportunity>().FirstAsync();
        opp.Title = "Opp title after customer delete";
        await harness.Db.SaveChangesAsync();

        Assert.Equal("Opp title after customer delete", (await harness.Opportunities.GetAllAsync())[0].Title);
    }
}