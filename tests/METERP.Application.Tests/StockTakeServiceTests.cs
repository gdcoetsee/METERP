using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class StockTakeServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private (AppDbContext Db, StockTakeService Service, InventoryService Inventory) CreateServices(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var service = new StockTakeService(db, inventory);
        return (db, service, inventory);
    }

    [Fact]
    public async Task StartSessionAsync_CreatesLinesForActiveItems()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "A-1",
                Name = "Widget",
                QuantityOnHand = 10,
                ReorderLevel = 2,
                UnitCost = 5m,
                IsActive = true
            });
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "B-1",
                Name = "Inactive",
                QuantityOnHand = 3,
                ReorderLevel = 1,
                UnitCost = 2m,
                IsActive = false
            });

            var sessionId = await service.StartSessionAsync(TestUserId);
            var session = await service.GetByIdAsync(sessionId);

            Assert.NotNull(session);
            Assert.Equal(StockTakeStatus.Open, session!.Status);
            Assert.Single(session.Lines);
            Assert.Equal(10, session.Lines.First().SystemQuantity);
        }
    }

    [Fact]
    public async Task PostSessionAsync_AppliesVarianceToInventory()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "C-1",
                Name = "Cable",
                QuantityOnHand = 20,
                ReorderLevel = 5,
                UnitCost = 10m,
                IsActive = true
            });

            var sessionId = await service.StartSessionAsync(TestUserId);
            await service.RecordCountAsync(sessionId, itemId, 18m);
            var posted = await service.PostSessionAsync(sessionId, TestUserId);

            Assert.True(posted);
            var item = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(18, item!.QuantityOnHand);

            var session = await service.GetByIdAsync(sessionId);
            Assert.Equal(StockTakeStatus.Posted, session!.Status);
        }
    }
}