using METERP.Application.Interfaces;
using METERP.Infrastructure.Caching;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantCacheInvalidationTests
{
    [Fact]
    public void OnCustomerMasterDataChanged_InvalidatesAllEmbeddedNavigationCategories()
    {
        var cache = new Mock<ITenantCacheService>();

        TenantCacheInvalidation.OnCustomerMasterDataChanged(cache.Object);

        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Customers), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Opportunities), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Quotes), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Jobs), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Invoices), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.SalesOrders), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Assets), Times.Once);
    }

    [Fact]
    public void OnSupplierMasterDataChanged_InvalidatesPurchaseOrderListCategory()
    {
        var cache = new Mock<ITenantCacheService>();

        TenantCacheInvalidation.OnSupplierMasterDataChanged(cache.Object);

        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Suppliers), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.PurchaseOrders), Times.Once);
    }

    [Fact]
    public void OnEmployeeMasterDataChanged_InvalidatesJobListCategory()
    {
        var cache = new Mock<ITenantCacheService>();

        TenantCacheInvalidation.OnEmployeeMasterDataChanged(cache.Object);

        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Employees), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Jobs), Times.Once);
    }

    [Fact]
    public void OnAssetMasterDataChanged_InvalidatesJobListCategory()
    {
        var cache = new Mock<ITenantCacheService>();

        TenantCacheInvalidation.OnAssetMasterDataChanged(cache.Object);

        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Assets), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Jobs), Times.Once);
    }

    [Fact]
    public async Task OnQuoteMutatedAsync_InvalidatesQuoteAndJobListCategories()
    {
        var cache = new Mock<ITenantCacheService>();

        await TenantCacheInvalidation.OnQuoteMutatedAsync(cache.Object);

        cache.Verify(c => c.InvalidateCategoryAsync(TenantCacheCategories.Quotes, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.InvalidateCategoryAsync(TenantCacheCategories.Jobs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnJobMutatedAsync_InvalidatesJobAndInvoiceListCategories()
    {
        var cache = new Mock<ITenantCacheService>();

        await TenantCacheInvalidation.OnJobMutatedAsync(cache.Object);

        cache.Verify(c => c.InvalidateCategoryAsync(TenantCacheCategories.Jobs, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.InvalidateCategoryAsync(TenantCacheCategories.Invoices, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OnIdentityMutated_InvalidatesUserAndRoleListCategories()
    {
        var cache = new Mock<ITenantCacheService>();

        TenantCacheInvalidation.OnIdentityMutated(cache.Object);

        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Users), Times.Once);
        cache.Verify(c => c.InvalidateCategory(TenantCacheCategories.Roles), Times.Once);
    }
}