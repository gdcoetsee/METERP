using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InventoryServiceTests
{
    private AppDbContext CreateContext()
    {
        var tenantId = Guid.NewGuid();
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
    public async Task CreateItemAsync_AssignsSku_WhenMissing()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var item = new InventoryItem { Name = "Cable 4mm", QuantityOnHand = 10, ReorderLevel = 3, UnitCost = 100m };

        var id = await service.CreateItemAsync(item);

        Assert.NotEqual(Guid.Empty, id);
        Assert.False(string.IsNullOrWhiteSpace(item.Sku));
        Assert.StartsWith("SKU-", item.Sku);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_UpdatesQuantityOnHand()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var item = new InventoryItem
        {
            Sku = "TEST-001",
            Name = "DB Board",
            QuantityOnHand = 10,
            ReorderLevel = 5,
            UnitCost = 500m
        };
        var id = await service.CreateItemAsync(item);

        await service.RecordStockTransactionAsync(id, -2, StockTransactionType.Issue, "J-TEST", null, "Job issue");

        var updated = await service.GetItemByIdAsync(id);
        Assert.NotNull(updated);
        Assert.Equal(8, updated.QuantityOnHand);

        var txns = await service.GetTransactionsForItemAsync(id);
        Assert.Single(txns);
        Assert.Equal(-2, txns[0].Quantity);
    }

    [Fact]
    public async Task GetAllItemsAsync_FiltersLowStockOnly()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        await service.CreateItemAsync(new InventoryItem { Sku = "OK", Name = "In stock", QuantityOnHand = 20, ReorderLevel = 5 });
        await service.CreateItemAsync(new InventoryItem { Sku = "LOW", Name = "Low stock", QuantityOnHand = 2, ReorderLevel = 5 });

        var lowOnly = await service.GetAllItemsAsync(lowStockOnly: true);

        Assert.Single(lowOnly);
        Assert.Equal("LOW", lowOnly[0].Sku);
    }

    [Fact]
    public async Task UpdateItemAsync_PersistsQuantityAndReorderLevel()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "UPD-001",
            Name = "Cable drum",
            QuantityOnHand = 12,
            ReorderLevel = 4,
            UnitCost = 85m
        });

        var item = await service.GetItemByIdAsync(id);
        Assert.NotNull(item);
        item!.QuantityOnHand = 20;
        item.ReorderLevel = 8;

        await service.UpdateItemAsync(item);

        var reloaded = await service.GetItemByIdAsync(id);
        Assert.NotNull(reloaded);
        Assert.Equal(20, reloaded!.QuantityOnHand);
        Assert.Equal(8, reloaded.ReorderLevel);
    }

    [Fact]
    public async Task GetAllItemsAsync_ExcludesInactiveItems()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var activeId = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "ACTIVE",
            Name = "Active fuse",
            QuantityOnHand = 15,
            ReorderLevel = 3,
            IsActive = true
        });

        var inactive = await service.GetItemByIdAsync(activeId);
        Assert.NotNull(inactive);
        inactive!.IsActive = false;
        await service.UpdateItemAsync(inactive);

        await service.CreateItemAsync(new InventoryItem
        {
            Sku = "VISIBLE",
            Name = "Visible item",
            QuantityOnHand = 10,
            ReorderLevel = 2,
            IsActive = true
        });

        var all = await service.GetAllItemsAsync();
        Assert.Single(all);
        Assert.Equal("VISIBLE", all[0].Sku);
    }
}