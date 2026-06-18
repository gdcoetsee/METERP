using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using METERP.Application.Interfaces;

namespace METERP.Infrastructure.Caching;

public class TenantMemoryCacheService : ITenantCacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _memoryCache;
    private readonly ITenantProvider _tenantProvider;
    private readonly ConcurrentDictionary<string, long> _generations = new();

    public TenantMemoryCacheService(IMemoryCache memoryCache, ITenantProvider tenantProvider)
    {
        _memoryCache = memoryCache;
        _tenantProvider = tenantProvider;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string category,
        string key,
        Func<Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken ct = default) where T : class
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        var generation = GetGeneration(tenantId, category);
        var cacheKey = $"{tenantId:N}:{category}:g{generation}:{key}";

        if (_memoryCache.TryGetValue(cacheKey, out T? cached) && cached != null)
            return cached;

        var value = await factory();
        _memoryCache.Set(cacheKey, value, absoluteExpiration ?? DefaultTtl);
        return value;
    }

    public Task InvalidateCategoryAsync(string category, CancellationToken ct = default)
    {
        InvalidateCategory(category);
        return Task.CompletedTask;
    }

    public void InvalidateCategory(string category)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        var genKey = GenerationKey(tenantId, category);
        _generations.AddOrUpdate(genKey, 1, (_, current) => current + 1);
    }

    private long GetGeneration(Guid tenantId, string category)
    {
        return _generations.GetOrAdd(GenerationKey(tenantId, category), 0);
    }

    private static string GenerationKey(Guid tenantId, string category) => $"{tenantId:N}:{category}";
}