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
    public async Task RecordCountAsync_ThrowsWhenCountedQuantityTooHigh()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "STK-HI",
                Name = "Widget",
                QuantityOnHand = 10,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordCountAsync(sessionId, itemId, 1_000_001m));
            Assert.Contains("1,000,000", ex.Message);
        }
    }

    [Fact]
    public async Task StartSessionAsync_ThrowsWhenNoActiveItems()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StartSessionAsync(TestUserId));
            Assert.Contains("no active inventory", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        await using (db)
        {
            Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task RecordCountAsync_ReturnsFalse_WhenLineMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "X-1",
                Name = "Spare",
                QuantityOnHand = 4,
                ReorderLevel = 1,
                UnitCost = 3m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);

            Assert.False(await service.RecordCountAsync(sessionId, Guid.NewGuid(), 4m));
            Assert.False(await service.RecordCountAsync(Guid.NewGuid(), itemId, 4m));
        }
    }

    [Fact]
    public async Task PostSessionAsync_ReturnsFalse_WhenSessionMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        await using (db)
        {
            Assert.False(await service.PostSessionAsync(Guid.NewGuid(), TestUserId));
        }
    }

    [Fact]
    public async Task RecordCountAsync_ReturnsFalse_WhenSessionPosted()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "P-1",
                Name = "Posted item",
                QuantityOnHand = 6,
                ReorderLevel = 1,
                UnitCost = 4m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);
            await service.RecordCountAsync(sessionId, itemId, 6m);
            await service.PostSessionAsync(sessionId, TestUserId);

            Assert.False(await service.RecordCountAsync(sessionId, itemId, 5m));
        }
    }

    [Fact]
    public async Task PostSessionAsync_ReturnsFalse_WhenAlreadyPosted()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "D-1",
                Name = "Drill",
                QuantityOnHand = 5,
                ReorderLevel = 1,
                UnitCost = 8m,
                IsActive = true
            });

            var sessionId = await service.StartSessionAsync(TestUserId);
            await service.RecordCountAsync(sessionId, itemId, 5m);
            await service.PostSessionAsync(sessionId, TestUserId);

            Assert.False(await service.PostSessionAsync(sessionId, TestUserId));
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

    [Fact]
    public async Task StartSessionAsync_ThrowsWhenOpenSessionExists()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "O-1",
                Name = "Open guard",
                QuantityOnHand = 1,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });

            await service.StartSessionAsync(TestUserId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StartSessionAsync(TestUserId));
        }
    }

    [Fact]
    public async Task RecordCountAsync_ThrowsWhenNegative()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "N-1",
                Name = "Neg",
                QuantityOnHand = 5,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordCountAsync(sessionId, itemId, -1m));
        }
    }

    [Fact]
    public async Task PostSessionAsync_ThrowsWhenNoCounts()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "Z-1",
                Name = "Zero counts",
                QuantityOnHand = 2,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PostSessionAsync(sessionId, TestUserId));
        }
    }

    [Fact]
    public async Task CancelSessionAsync_CancelsWithoutInventoryChange()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "K-1",
                Name = "Keep",
                QuantityOnHand = 12,
                ReorderLevel = 0,
                UnitCost = 3m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);
            await service.RecordCountAsync(sessionId, itemId, 9m);

            Assert.True(await service.CancelSessionAsync(sessionId, TestUserId, "Wrong timing"));
            var session = await service.GetByIdAsync(sessionId);
            Assert.Equal(StockTakeStatus.Cancelled, session!.Status);
            Assert.Contains("Wrong timing", session.Notes);

            var item = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(12m, item!.QuantityOnHand);

            // After cancel, a new session may start.
            var nextId = await service.StartSessionAsync(TestUserId);
            Assert.NotEqual(sessionId, nextId);
        }
    }

    [Fact]
    public async Task StartSessionAsync_RejectsNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "N-1",
                Name = "Note item",
                QuantityOnHand = 1,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StartSessionAsync(TestUserId, new string('N', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CancelSessionAsync_RejectsReasonTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "C-1",
                Name = "Cancel item",
                QuantityOnHand = 1,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                service.CancelSessionAsync(sessionId, TestUserId, new string('R', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CancelSessionAsync_AcceptsReasonAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "C-OK",
                Name = "Cancel ok",
                QuantityOnHand = 1,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId);
            Assert.True(await service.CancelSessionAsync(sessionId, TestUserId, new string('R', 500)));
            var session = await service.GetByIdAsync(sessionId);
            Assert.Equal(StockTakeStatus.Cancelled, session!.Status);
        }
    }

    [Fact]
    public async Task StartSessionAsync_AcceptsNotesAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "N-OK",
                Name = "Note ok",
                QuantityOnHand = 1,
                ReorderLevel = 0,
                UnitCost = 1m,
                IsActive = true
            });
            var sessionId = await service.StartSessionAsync(TestUserId, new string('N', 500));
            var session = await service.GetByIdAsync(sessionId);
            Assert.Equal(500, session!.Notes!.Length);
        }
    }

    [Fact]
    public async Task GetVarianceSummaryAsync_ReflectsCountsAndGainsLosses()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        await using (db)
        {
            var gainId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "G-1",
                Name = "Gain",
                QuantityOnHand = 10,
                IsActive = true
            });
            var lossId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "L-1",
                Name = "Loss",
                QuantityOnHand = 20,
                IsActive = true
            });
            await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "U-1",
                Name = "Uncounted",
                QuantityOnHand = 5,
                IsActive = true
            });

            var sessionId = await service.StartSessionAsync(TestUserId);
            await service.RecordCountAsync(sessionId, gainId, 12m);
            await service.RecordCountAsync(sessionId, lossId, 15m);

            var summary = await service.GetVarianceSummaryAsync(sessionId);
            Assert.NotNull(summary);
            Assert.Equal(2, summary!.LinesCounted);
            Assert.Equal(1, summary.LinesUncounted);
            Assert.Equal(2, summary.LinesWithVariance);
            Assert.Equal(2m, summary.TotalPositiveVariance);
            Assert.Equal(-5m, summary.TotalNegativeVariance);
        }
    }
}