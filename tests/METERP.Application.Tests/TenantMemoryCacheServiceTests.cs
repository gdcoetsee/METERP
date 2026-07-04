using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using METERP.Application.Interfaces;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantMemoryCacheServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReturnsCachedValueUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new TenantMemoryCacheService(cache, tenantProvider.Object);

        var callCount = 0;
        var first = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
        {
            callCount++;
            return Task.FromResult("value-1");
        });

        var second = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
        {
            callCount++;
            return Task.FromResult("value-2");
        });

        Assert.Equal("value-1", first);
        Assert.Equal("value-1", second);
        Assert.Equal(1, callCount);

        service.InvalidateCategory(TenantCacheCategories.Quotes);

        var third = await service.GetOrCreateAsync("quotes", "p1:s20", () =>
        {
            callCount++;
            return Task.FromResult("value-3");
        });

        Assert.Equal("value-3", third);
        Assert.Equal(2, callCount);
    }
}