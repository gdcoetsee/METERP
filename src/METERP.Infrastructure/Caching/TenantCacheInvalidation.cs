using METERP.Application.Interfaces;

namespace METERP.Infrastructure.Caching;

/// <summary>
/// Cross-module list-cache invalidation when master data or spine documents change.
/// Keeps embedded navigation properties fresh in cached JSON payloads.
/// </summary>
public static class TenantCacheInvalidation
{
    public static void OnCustomerMasterDataChanged(ITenantCacheService cache) =>
        Invalidate(
            cache,
            TenantCacheCategories.Customers,
            TenantCacheCategories.Opportunities,
            TenantCacheCategories.Quotes,
            TenantCacheCategories.Jobs,
            TenantCacheCategories.Invoices,
            TenantCacheCategories.SalesOrders,
            TenantCacheCategories.Assets);

    public static void OnSupplierMasterDataChanged(ITenantCacheService cache) =>
        Invalidate(
            cache,
            TenantCacheCategories.Suppliers,
            TenantCacheCategories.PurchaseOrders);

    public static void OnEmployeeMasterDataChanged(ITenantCacheService cache) =>
        Invalidate(
            cache,
            TenantCacheCategories.Employees,
            TenantCacheCategories.Jobs);

    public static void OnAssetMasterDataChanged(ITenantCacheService cache) =>
        Invalidate(
            cache,
            TenantCacheCategories.Assets,
            TenantCacheCategories.Jobs);

    public static Task OnQuoteMutatedAsync(ITenantCacheService cache, CancellationToken ct = default) =>
        InvalidateAsync(
            cache,
            ct,
            TenantCacheCategories.Quotes,
            TenantCacheCategories.Jobs);

    public static Task OnJobMutatedAsync(ITenantCacheService cache, CancellationToken ct = default) =>
        InvalidateAsync(
            cache,
            ct,
            TenantCacheCategories.Jobs,
            TenantCacheCategories.Invoices);

    public static void OnIdentityMutated(ITenantCacheService cache) =>
        Invalidate(
            cache,
            TenantCacheCategories.Users,
            TenantCacheCategories.Roles);

    private static void Invalidate(ITenantCacheService cache, params string[] categories)
    {
        foreach (var category in categories)
            cache.InvalidateCategory(category);
    }

    private static async Task InvalidateAsync(
        ITenantCacheService cache,
        CancellationToken ct,
        params string[] categories)
    {
        foreach (var category in categories)
            await cache.InvalidateCategoryAsync(category, ct);
    }
}