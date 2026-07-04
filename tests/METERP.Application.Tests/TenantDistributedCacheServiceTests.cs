using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantDistributedCacheServiceTests
{
    private static (TenantDistributedCacheService Service, ServiceProvider Provider) CreateService(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddSingleton(tenantProvider.Object);
        services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 60);

        var provider = services.BuildServiceProvider();
        var distributed = provider.GetRequiredService<IDistributedCache>();
        var service = new TenantDistributedCacheService(
            distributed,
            tenantProvider.Object,
            provider.GetRequiredService<IOptions<CacheOptions>>());

        return (service, provider);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsCachedValueUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (service, provider) = CreateService(tenantId);
        using (provider)
        {
            var callCount = 0;
            var first = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
            {
                callCount++;
                return Task.FromResult(new List<string> { "value-1" });
            });

            var second = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
            {
                callCount++;
                return Task.FromResult(new List<string> { "value-2" });
            });

            Assert.Single(first);
            Assert.Equal("value-1", first[0]);
            Assert.Equal(first[0], second[0]);
            Assert.Equal(1, callCount);

            service.InvalidateCategory(TenantCacheCategories.Quotes);

            var third = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
            {
                callCount++;
                return Task.FromResult(new List<string> { "value-3" });
            });

            Assert.Equal("value-3", third[0]);
            Assert.Equal(2, callCount);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_SerializesQuotesWithLinesWithoutCycle()
    {
        var tenantId = Guid.NewGuid();
        var (service, provider) = CreateService(tenantId);
        using (provider)
        {
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                QuoteNumber = "Q-SER-001",
                Lines =
                {
                    new QuoteLine { Description = "Travel", Quantity = 1, UnitPrice = 620m }
                }
            };
            quote.Lines.First().Quote = quote;
            ListCacheGraphHelper.PrepareQuotesForCache(new[] { quote });

            var cached = await service.GetOrCreateAsync<List<Quote>>(
                "quotes",
                "p1:s20",
                () => Task.FromResult(new List<Quote> { quote }));

            var second = await service.GetOrCreateAsync<List<Quote>>(
                "quotes",
                "p1:s20",
                () => throw new InvalidOperationException("Factory should not run on cache hit"));

            Assert.Equal("Q-SER-001", second[0].QuoteNumber);
            Assert.Single(second[0].Lines);
            Assert.Equal(cached[0].Lines.First().Description, second[0].Lines.First().Description);
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_IsolatesTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (serviceA, providerA) = CreateService(tenantA);
        var (serviceB, providerB) = CreateService(tenantB);

        using (providerA)
        using (providerB)
        {
            var valueA = await serviceA.GetOrCreateAsync("jobs", "p1:s10", () => Task.FromResult("tenant-a"));
            var valueB = await serviceB.GetOrCreateAsync("jobs", "p1:s10", () => Task.FromResult("tenant-b"));

            Assert.Equal("tenant-a", valueA);
            Assert.Equal("tenant-b", valueB);
        }
    }
}