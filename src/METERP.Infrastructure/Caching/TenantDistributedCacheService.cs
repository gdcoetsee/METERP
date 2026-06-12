using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;

namespace METERP.Infrastructure.Caching;

/// <summary>
/// Tenant-scoped list cache using IDistributedCache (Redis in production, memory for local/demo).
/// Generation bumps invalidate categories without scanning keys — safe for multi-instance when Redis is enabled.
/// </summary>
public class TenantDistributedCacheService : ITenantCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly IDistributedCache _distributed;
    private readonly ITenantProvider _tenantProvider;
    private readonly TimeSpan _defaultTtl;

    public TenantDistributedCacheService(
        IDistributedCache distributed,
        ITenantProvider tenantProvider,
        IOptions<CacheOptions>? options = null)
    {
        _distributed = distributed;
        _tenantProvider = tenantProvider;
        var seconds = options?.Value.DefaultTtlSeconds ?? 60;
        _defaultTtl = TimeSpan.FromSeconds(seconds > 0 ? seconds : 60);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string category,
        string key,
        Func<Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken ct = default) where T : class
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        var generation = await GetGenerationAsync(tenantId, category, ct);
        var cacheKey = BuildValueKey(tenantId, category, generation, key);

        var existing = await _distributed.GetStringAsync(cacheKey, ct);
        if (!string.IsNullOrEmpty(existing))
        {
            var deserialized = JsonSerializer.Deserialize<T>(existing, JsonOptions);
            if (deserialized != null)
                return deserialized;
        }

        var value = await factory();
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await _distributed.SetStringAsync(
            cacheKey,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpiration ?? _defaultTtl
            },
            ct);

        return value;
    }

    public void InvalidateCategory(string category)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        // Write path already async in services; sync invalidate keeps ITenantCacheService unchanged.
        InvalidateCategoryAsync(tenantId, category, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task InvalidateCategoryAsync(Guid tenantId, string category, CancellationToken ct)
    {
        var genKey = BuildGenerationKey(tenantId, category);
        var current = await _distributed.GetStringAsync(genKey, ct);
        var next = (long.TryParse(current, out var g) ? g : 0) + 1;
        await _distributed.SetStringAsync(
            genKey,
            next.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
            ct);
    }

    private async Task<long> GetGenerationAsync(Guid tenantId, string category, CancellationToken ct)
    {
        var genKey = BuildGenerationKey(tenantId, category);
        var current = await _distributed.GetStringAsync(genKey, ct);
        return long.TryParse(current, out var g) ? g : 0;
    }

    private static string BuildGenerationKey(Guid tenantId, string category) =>
        $"meterp:gen:{tenantId:N}:{category}";

    private static string BuildValueKey(Guid tenantId, string category, long generation, string key) =>
        $"meterp:val:{tenantId:N}:{category}:g{generation}:{key}";
}