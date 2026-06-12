using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PurchaseOrderServiceTests
{
    private (AppDbContext Db, PurchaseOrderService Service, InventoryService Inventory) CreateServices(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var service = new PurchaseOrderService(db, inventory);
        return (db, service, inventory);
    }

    [Fact]
    public async Task CreateAsync_AssignsPoNumber_AndRecalculatesTotals()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Cable Co" });

            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0.15m,
                Lines =
                {
                    new PurchaseOrderLine { Description = "Cable 4mm", Quantity = 10, UnitPrice = 50m }
                }
            };

            var id = await service.CreateAsync(po);
            var loaded = await service.GetByIdAsync(id);

            Assert.NotNull(loaded);
            Assert.StartsWith("PO-", loaded.PoNumber);
            Assert.Equal(500m, loaded.Subtotal);
            Assert.Equal(75m, loaded.Tax);
            Assert.Equal(575m, loaded.Total);
        }
    }

    [Fact]
    public async Task AddLineAsync_RecalculatesParentTotals()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder { SupplierId = supplierId, TaxRate = 0.15m });

            await service.AddLineAsync(new PurchaseOrderLine
            {
                PurchaseOrderId = poId,
                Description = "DB Board",
                Quantity = 2,
                UnitPrice = 1200m
            });

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(2400m, loaded!.Subtotal);
            Assert.Equal(360m, loaded.Tax);
            Assert.Equal(2760m, loaded.Total);
        }
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesPoAndLines()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                Lines = { new PurchaseOrderLine { Description = "Line 1", Quantity = 1, UnitPrice = 100m } }
            };
            var poId = await service.CreateAsync(po);
            var lineId = po.Lines.First().Id;

            await service.DeleteAsync(poId);

            Assert.Null(await service.GetByIdAsync(poId));
            var deletedLine = await db.Set<PurchaseOrderLine>().IgnoreQueryFilters().FirstAsync(l => l.Id == lineId);
            Assert.True(deletedLine.IsDeleted);
        }
    }

    [Fact]
    public async Task ReceiveAsync_UpdatesInventory_AndSetsStatusReceived()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Wholesaler" });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "CABLE-4",
                Name = "Cable 4mm",
                QuantityOnHand = 5,
                ReorderLevel = 2,
                UnitCost = 40m
            });

            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0.15m,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Description = "Cable restock",
                        Quantity = 20,
                        UnitPrice = 40m,
                        InventoryItemId = itemId
                    }
                }
            });

            await service.ReceiveAsync(poId);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Received, loaded!.Status);

            var item = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(25, item!.QuantityOnHand);
        }
    }

    [Fact]
    public async Task UpdateLineAsync_RecalculatesParentTotals()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });

            var line = new PurchaseOrderLine { Description = "Fuse", Quantity = 2, UnitPrice = 100m };
            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0.15m,
                Lines = { line }
            };
            var poId = await service.CreateAsync(po);

            line.Quantity = 5;
            await service.UpdateLineAsync(line);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(500m, loaded!.Subtotal);
            Assert.Equal(575m, loaded.Total);
        }
    }

    [Fact]
    public async Task DeleteLineAsync_SoftDeletesAndRecalculates()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });

            var keepLine = new PurchaseOrderLine { Description = "Keep", Quantity = 1, UnitPrice = 200m };
            var removeLine = new PurchaseOrderLine { Description = "Remove", Quantity = 3, UnitPrice = 50m };
            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines = { keepLine, removeLine }
            };
            var poId = await service.CreateAsync(po);

            await service.DeleteLineAsync(removeLine.Id);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(200m, loaded!.Subtotal);

            var deletedLine = await db.Set<PurchaseOrderLine>()
                .IgnoreQueryFilters()
                .FirstAsync(l => l.Id == removeLine.Id);
            Assert.True(deletedLine.IsDeleted);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsPurchaseOrderStatus()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder { SupplierId = supplierId, TaxRate = 0m });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Sent, loaded!.Status);
        }
    }

    [Fact]
    public async Task ReceiveAsync_WhenPoNotFound_DoesNotThrow()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            await service.ReceiveAsync(Guid.NewGuid());
        }
    }

    [Fact]
    public async Task ReceiveAsync_WithoutInventoryLinks_StillMarksReceived()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                {
                    new PurchaseOrderLine { Description = "Consumables", Quantity = 3, UnitPrice = 25m }
                }
            });

            await service.ReceiveAsync(poId);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Received, loaded!.Status);
            Assert.Empty(await inventory.GetRecentTransactionsAsync(10));
        }
    }

    [Fact]
    public async Task ReceiveAsync_OnlyReceiptsLinkedInventoryLines()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var linkedItemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "LINK-01",
                Name = "Linked part",
                QuantityOnHand = 4,
                ReorderLevel = 1,
                UnitCost = 10m
            });

            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Description = "Linked part",
                        Quantity = 6,
                        UnitPrice = 10m,
                        InventoryItemId = linkedItemId
                    },
                    new PurchaseOrderLine { Description = "Office supplies", Quantity = 2, UnitPrice = 50m }
                }
            });

            await service.ReceiveAsync(poId);

            var item = await inventory.GetItemByIdAsync(linkedItemId);
            Assert.Equal(10, item!.QuantityOnHand);

            var txs = await inventory.GetRecentTransactionsAsync(5);
            Assert.Single(txs);
            Assert.Equal(6, txs[0].Quantity);
        }
    }
}