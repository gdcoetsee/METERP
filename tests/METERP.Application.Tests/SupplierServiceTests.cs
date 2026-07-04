using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class SupplierServiceTests
{
    private AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task CreateAsync_PersistsSupplier()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        var supplier = new Supplier { Name = "Cable Wholesaler", ContactPerson = "Jane" };

        var id = await service.CreateAsync(supplier);

        Assert.NotEqual(Guid.Empty, id);
        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Cable Wholesaler", loaded.Name);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesInactiveSuppliers()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        await service.CreateAsync(new Supplier { Name = "Active Co", IsActive = true });
        await service.CreateAsync(new Supplier { Name = "Inactive Co", IsActive = false });

        var results = await service.GetAllAsync();

        Assert.Single(results);
        Assert.Equal("Active Co", results[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        await service.CreateAsync(new Supplier { Name = "Panel Supplies", Email = "sales@panel.test" });
        await service.CreateAsync(new Supplier { Name = "Other Vendor", ContactPerson = "Bob" });

        var results = await service.GetAllAsync("panel");

        Assert.Single(results);
        Assert.Equal("Panel Supplies", results[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesSupplier()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        var id = await service.CreateAsync(new Supplier { Name = "To Remove" });

        await service.DeleteAsync(id);

        Assert.Null(await service.GetByIdAsync(id));

        var deleted = await db.Set<Supplier>().IgnoreQueryFilters().FirstAsync(s => s.Id == id);
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenSupplierSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        var id = await service.CreateAsync(new Supplier { Name = "Gone Vendor" });

        await service.DeleteAsync(id);

        Assert.Null(await service.GetByIdAsync(id));
    }

    [Fact]
    public async Task UpdateAsync_PersistsContactAndEmail()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        var id = await service.CreateAsync(new Supplier { Name = "Cable Co", ContactPerson = "Sam" });

        var supplier = await service.GetByIdAsync(id);
        Assert.NotNull(supplier);
        supplier!.ContactPerson = "Jane Doe";
        supplier.Email = "jane@cable.test";

        await service.UpdateAsync(supplier);

        var reloaded = await service.GetByIdAsync(id);
        Assert.Equal("Jane Doe", reloaded!.ContactPerson);
        Assert.Equal("jane@cable.test", reloaded.Email);
    }
}