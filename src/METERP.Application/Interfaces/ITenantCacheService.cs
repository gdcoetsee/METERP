namespace METERP.Application.Interfaces;

/// <summary>
/// Tenant-scoped in-memory cache for hot read paths (lists, dashboards).
/// Invalidated on writes via category bumps.
/// </summary>
public interface ITenantCacheService
{
    Task<T> GetOrCreateAsync<T>(
        string category,
        string key,
        Func<Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken ct = default) where T : class;

    void InvalidateCategory(string category);
}