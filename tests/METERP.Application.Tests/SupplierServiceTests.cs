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
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier { Name = "Note Supplier", Notes = new string('N', 2001) }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier
            {
                Name = "Email Supplier",
                Email = new string('a', 195) + "@x.com"
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPhoneTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier
            {
                Name = "Phone Supplier",
                Phone = new string('1', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenContactPersonTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier
            {
                Name = "Contact Supplier",
                ContactPerson = new string('C', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenTaxNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier
            {
                Name = "Tax Supplier",
                TaxNumber = new string('T', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCityTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier
            {
                Name = "City Supplier",
                City = new string('C', 101)
            }));
        Assert.Contains("100 characters", ex.Message);
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

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameDuplicateAmongActive()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        await service.CreateAsync(new Supplier { Name = "Acme Cable" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier { Name = "Acme Cable" }));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailDuplicateAmongActive()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        await service.CreateAsync(new Supplier { Name = "Cable A", Email = "orders@cable.demo" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier { Name = "Cable B", Email = "orders@cable.demo" }));
        Assert.Contains("email", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenEmailInvalid()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Supplier { Name = "Bad Mail Sup", Email = "not-an-email" }));
        Assert.Contains("valid address", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenSupplierHasOpenPurchaseOrders()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new SupplierService(db);
        var id = await service.CreateAsync(new Supplier { Name = "Busy Sup" });

        db.Set<PurchaseOrder>().Add(new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = id,
            PoNumber = "PO-OPEN",
            Status = PurchaseOrderStatus.Sent
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(id));
        Assert.Contains("open purchase", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await service.GetByIdAsync(id));
    }
}