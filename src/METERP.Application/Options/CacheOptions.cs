namespace METERP.Application.Options;

/// <summary>
/// Distributed cache settings. When Redis is not configured, falls back to in-process distributed memory.
/// </summary>
public class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>StackExchange.Redis connection string. Empty = use distributed memory cache (single-node).</summary>
    public string? RedisConnection { get; set; }

    public int DefaultTtlSeconds { get; set; } = 60;
}