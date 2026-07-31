using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class PurchaseOrderServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private (AppDbContext Db, PurchaseOrderService Service, InventoryService Inventory) CreateServices(Guid tenantId)
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
        var service = new PurchaseOrderService(db, inventory);
        return (db, service, inventory);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPoNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new PurchaseOrder
                {
                    SupplierId = supplierId,
                    PoNumber = new string('P', 51),
                    TaxRate = 0m
                }));
            Assert.Contains("50 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenPoNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Cable Co" });
            await db.SaveChangesAsync();

            await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                PoNumber = "PO-DUP-1",
                TaxRate = 0m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new PurchaseOrder
                {
                    SupplierId = supplierId,
                    PoNumber = "PO-DUP-1",
                    TaxRate = 0m
                }));
            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
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

            var grv = await ReceiveSentPoAsync(service, poId);

            Assert.NotNull(grv);
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
    public async Task GetAllAsync_FiltersBySupplierName()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var electroId = Guid.NewGuid();
            var panelId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = electroId, TenantId = tenantId, Name = "ElectroSupply SA" });
            db.Set<Supplier>().Add(new Supplier { Id = panelId, TenantId = tenantId, Name = "Panel Supplies CC" });

            await service.CreateAsync(new PurchaseOrder { SupplierId = electroId, TaxRate = 0m });
            await service.CreateAsync(new PurchaseOrder { SupplierId = panelId, TaxRate = 0m });

            var results = await service.GetAllAsync("electro");

            Assert.Single(results);
            Assert.Equal("ElectroSupply SA", results[0].Supplier?.Name);
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenPoNotDraft()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines = { new PurchaseOrderLine { Description = "A", Quantity = 1, UnitPrice = 1m } }
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new PurchaseOrderLine
                {
                    PurchaseOrderId = poId,
                    Description = "Late line",
                    Quantity = 1,
                    UnitPrice = 5m
                }));
        }
    }

    [Fact]
    public async Task UpdateAsync_ThrowsWhenPoNotDraft()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0.15m,
                Notes = "Draft notes",
                Lines = { new PurchaseOrderLine { Description = "Part", Quantity = 1, UnitPrice = 10m } }
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var po = await service.GetByIdAsync(poId);
            Assert.NotNull(po);
            po!.Notes = "Should not save";

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(po));
        }
    }

    [Fact]
    public async Task UpdateAsync_AllowsDraftNotesAndPreservesPoNumber()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0.15m,
                Notes = "Original"
            });
            var before = await service.GetByIdAsync(poId);
            Assert.NotNull(before);
            var number = before!.PoNumber;

            before.Notes = "Updated notes";
            before.PoNumber = "HACKED";
            before.Status = PurchaseOrderStatus.Sent;
            await service.UpdateAsync(before);

            var after = await service.GetByIdAsync(poId);
            Assert.Equal("Updated notes", after!.Notes);
            Assert.Equal(number, after.PoNumber);
            Assert.Equal(PurchaseOrderStatus.Draft, after.Status);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new PurchaseOrder
                {
                    SupplierId = supplierId,
                    PoDate = DateTime.UtcNow.Date,
                    TaxRate = 0.15m,
                    Notes = new string('N', 2001)
                }));
            Assert.Contains("2000 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_AcceptsNotesAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();

            var id = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                PoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m,
                Notes = new string('N', 2000)
            });
            var saved = await db.Set<PurchaseOrder>().FirstAsync(p => p.Id == id);
            Assert.Equal(2000, saved.Notes!.Length);
        }
    }

    [Fact]
    public async Task AddLineAsync_AcceptsDescriptionAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                PoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m
            });

            var lineId = await service.AddLineAsync(new PurchaseOrderLine
            {
                PurchaseOrderId = poId,
                Description = new string('D', 500),
                Quantity = 1,
                UnitPrice = 10m
            });
            var line = await db.Set<PurchaseOrderLine>().FirstAsync(l => l.Id == lineId);
            Assert.Equal(500, line.Description.Length);
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenUnitTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                PoDate = DateTime.UtcNow.Date,
                TaxRate = 0.15m
            });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new PurchaseOrderLine
                {
                    PurchaseOrderId = poId,
                    Description = "Cable",
                    Quantity = 1,
                    UnitPrice = 10m,
                    Unit = new string('U', 21)
                }));
            Assert.Contains("20 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenExpectedDateBeforePoDate()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync(new PurchaseOrder
                {
                    SupplierId = supplierId,
                    TaxRate = 0.15m,
                    PoDate = DateTime.UtcNow.Date,
                    ExpectedDate = DateTime.UtcNow.Date.AddDays(-2)
                }));
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenQuantityNotPositive()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder { SupplierId = supplierId, TaxRate = 0m });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new PurchaseOrderLine
                {
                    PurchaseOrderId = poId,
                    Description = "Cable",
                    Quantity = -1,
                    UnitPrice = 10m
                }));
        }
    }

    [Fact]
    public async Task AddLineAsync_ThrowsWhenDescriptionMissing()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder { SupplierId = supplierId, TaxRate = 0m });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddLineAsync(new PurchaseOrderLine
                {
                    PurchaseOrderId = poId,
                    Description = "",
                    Quantity = 1,
                    UnitPrice = 10m
                }));
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
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines = { new PurchaseOrderLine { Description = "Line", Quantity = 1, UnitPrice = 10m } }
            });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Sent, loaded!.Status);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_ThrowsWhenSendingEmptyPo()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var poId = await service.CreateAsync(new PurchaseOrder { SupplierId = supplierId, TaxRate = 0m });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent));
            Assert.Contains("no lines", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_Cancel_UnhooksLinkedRequisition()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var customerId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "C" });
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customerId,
                JobNumber = "J-PO-CANCEL",
                Title = "Work",
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines = { new PurchaseOrderLine { Description = "Cable", Quantity = 1, UnitPrice = 10m } }
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var req = new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequisitionNumber = "REQ-PO-1",
                Status = RequisitionStatus.ProcurementOrdered,
                PurchaseOrderId = poId
            };
            db.Set<StockRequisition>().Add(req);
            await db.SaveChangesAsync();

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Cancelled);

            var reloaded = await db.Set<StockRequisition>().FirstAsync(r => r.Id == req.Id);
            Assert.Null(reloaded.PurchaseOrderId);
            Assert.Equal(RequisitionStatus.AwaitingProcurement, reloaded.Status);
            Assert.Equal(PurchaseOrderStatus.Cancelled, (await service.GetByIdAsync(poId))!.Status);
        }
    }

    [Fact]
    public async Task ReceiveAsync_WhenPoNotFound_DoesNotThrow()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var result = await service.ReceiveAsync(Guid.NewGuid(), TestUserId);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task ReceiveAsync_WithoutInventoryLinks_CreatesGrvButNoStockTxn()
    {
        // Non-catalog PO lines (no InventoryItemId) still get a GRV so free-text REQ procurement can complete.
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

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            var grv = await service.ReceiveAsync(poId, TestUserId);

            Assert.NotNull(grv);
            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Received, loaded!.Status);
            Assert.Equal(3m, loaded.Lines.First().QuantityReceived);
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

            var grv = await ReceiveSentPoAsync(service, poId);

            Assert.NotNull(grv);
            var item = await inventory.GetItemByIdAsync(linkedItemId);
            Assert.Equal(10, item!.QuantityOnHand);

            var txs = await inventory.GetRecentTransactionsAsync(5);
            Assert.Single(txs);
            Assert.Equal(6, txs[0].Quantity);
        }
    }

    [Fact]
    public async Task ReceiveAsync_RejectsDeliveryNoteTooLong()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                Status = PurchaseOrderStatus.Draft,
                TaxRate = 0m,
                Lines = [new PurchaseOrderLine { Description = "Cable", Quantity = 1, UnitPrice = 10m }]
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReceiveAsync(poId, TestUserId, new string('D', 101)));
            Assert.Contains("100 characters", ex.Message);
        }
    }

    [Fact]
    public async Task ReceiveAsync_AcceptsDeliveryNoteAt100Characters()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, _) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                Status = PurchaseOrderStatus.Draft,
                TaxRate = 0m,
                Lines = [new PurchaseOrderLine { Description = "Cable", Quantity = 1, UnitPrice = 10m }]
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

            var grv = await service.ReceiveAsync(poId, TestUserId, new string('D', 100));
            Assert.NotNull(grv);
            Assert.Equal(100, grv!.SupplierDeliveryNote!.Length);
        }
    }

    [Fact]
    public async Task ReceiveAsync_RejectsLineQuantityTooHigh()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "RCV-HI",
                Name = "Cable",
                QuantityOnHand = 0,
                UnitCost = 10m,
                IsActive = true
            });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                [
                    new PurchaseOrderLine
                    {
                        Description = "Cable",
                        Quantity = 5,
                        UnitPrice = 10m,
                        InventoryItemId = itemId
                    }
                ]
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            var lineId = (await db.Set<PurchaseOrderLine>().FirstAsync(l => l.PurchaseOrderId == poId)).Id;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReceiveAsync(poId, TestUserId, null, new Dictionary<Guid, decimal> { [lineId] = 1_000_001m }));
            Assert.Contains("1,000,000", ex.Message);
        }
    }

    [Fact]
    public async Task ReceiveAsync_RejectsNegativeLineQuantity()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "S", IsActive = true });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "RCV-NEG",
                Name = "Cable",
                QuantityOnHand = 0,
                UnitCost = 10m,
                IsActive = true
            });
            await db.SaveChangesAsync();
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                [
                    new PurchaseOrderLine
                    {
                        Description = "Cable",
                        Quantity = 5,
                        UnitPrice = 10m,
                        InventoryItemId = itemId
                    }
                ]
            });
            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            var lineId = (await db.Set<PurchaseOrderLine>().FirstAsync(l => l.PurchaseOrderId == poId)).Id;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReceiveAsync(poId, TestUserId, null, new Dictionary<Guid, decimal> { [lineId] = -1m }));
            Assert.Contains("negative", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReceiveAsync_WhenDraft_Throws()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Sup" });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "X-1",
                Name = "Part",
                QuantityOnHand = 0,
                ReorderLevel = 1,
                UnitCost = 5m
            });

            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Description = "Part",
                        Quantity = 5,
                        UnitPrice = 5m,
                        InventoryItemId = itemId
                    }
                }
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReceiveAsync(poId, TestUserId));
        }
    }

    [Fact]
    public async Task ReceiveAsync_PartialQuantities_SetsPartiallyReceived()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "Partial Sup" });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "PART-01",
                Name = "Partial part",
                QuantityOnHand = 0,
                ReorderLevel = 1,
                UnitCost = 10m
            });

            var line = new PurchaseOrderLine
            {
                Description = "Partial part",
                Quantity = 10,
                UnitPrice = 10m,
                InventoryItemId = itemId
            };
            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines = { line }
            });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            var grv = await service.ReceiveAsync(
                poId,
                TestUserId,
                supplierDeliveryNote: "DN-PARTIAL-1",
                lineQuantities: new Dictionary<Guid, decimal> { [line.Id] = 4m });

            Assert.NotNull(grv);
            Assert.Equal("DN-PARTIAL-1", grv!.SupplierDeliveryNote);

            var loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.PartiallyReceived, loaded!.Status);
            Assert.Equal(4m, loaded.Lines.First().QuantityReceived);

            var item = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(4m, item!.QuantityOnHand);

            // Second GRV completes receipt
            var grv2 = await service.ReceiveAsync(
                poId,
                TestUserId,
                supplierDeliveryNote: "DN-PARTIAL-2",
                lineQuantities: new Dictionary<Guid, decimal> { [line.Id] = 6m });

            Assert.NotNull(grv2);
            loaded = await service.GetByIdAsync(poId);
            Assert.Equal(PurchaseOrderStatus.Received, loaded!.Status);
            Assert.Equal(10m, loaded.Lines.First().QuantityReceived);
            item = await inventory.GetItemByIdAsync(itemId);
            Assert.Equal(10m, item!.QuantityOnHand);
        }
    }

    [Fact]
    public async Task GetRecentGrvsAsync_ReturnsDeliveryNote()
    {
        var tenantId = Guid.NewGuid();
        var (db, service, inventory) = CreateServices(tenantId);
        using (db)
        {
            var supplierId = Guid.NewGuid();
            db.Set<Supplier>().Add(new Supplier { Id = supplierId, TenantId = tenantId, Name = "DN Sup" });
            var itemId = await inventory.CreateItemAsync(new InventoryItem
            {
                Sku = "DN-1",
                Name = "DN item",
                QuantityOnHand = 0,
                ReorderLevel = 1,
                UnitCost = 5m
            });

            var poId = await service.CreateAsync(new PurchaseOrder
            {
                SupplierId = supplierId,
                TaxRate = 0m,
                Lines =
                {
                    new PurchaseOrderLine
                    {
                        Description = "DN item",
                        Quantity = 2,
                        UnitPrice = 5m,
                        InventoryItemId = itemId
                    }
                }
            });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            await service.ReceiveAsync(poId, TestUserId, "WAYBILL-99");

            var grvs = await service.GetRecentGrvsAsync(10);
            Assert.Contains(grvs, g => g.SupplierDeliveryNote == "WAYBILL-99");

            var forPo = await service.GetGrvsForPurchaseOrderAsync(poId);
            Assert.Single(forPo);
            Assert.Equal("WAYBILL-99", forPo[0].SupplierDeliveryNote);
        }
    }

    private static async Task<GoodsReceiptVoucher?> ReceiveSentPoAsync(PurchaseOrderService service, Guid poId)
    {
        await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
        return await service.ReceiveAsync(poId, TestUserId);
    }

    [Fact]
    public async Task CreateSkuFromPoLineAsync_LinksInventory_AndBackfillsReceivedQty()
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
                    new PurchaseOrderLine
                    {
                        Description = "50mm galvanised conduit special",
                        Quantity = 10,
                        UnitPrice = 42.5m,
                        Unit = "m"
                    }
                }
            });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            await service.ReceiveAsync(poId, TestUserId);

            var po = await service.GetByIdAsync(poId);
            var line = po!.Lines.Single();
            Assert.Null(line.InventoryItemId);
            Assert.Equal(10m, line.QuantityReceived);

            var itemId = await service.CreateSkuFromPoLineAsync(line.Id, sku: "COND-50-GALV", category: "Electrical");
            var item = await inventory.GetItemByIdAsync(itemId);
            Assert.NotNull(item);
            Assert.Equal("COND-50-GALV", item!.Sku);
            Assert.Equal("50mm galvanised conduit special", item.Name);
            Assert.Equal(10m, item.QuantityOnHand);
            Assert.Equal(42.5m, item.UnitCost);
            Assert.Equal("Electrical", item.Category);

            po = await service.GetByIdAsync(poId);
            Assert.Equal(itemId, po!.Lines.Single().InventoryItemId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateSkuFromPoLineAsync(line.Id));
        }
    }

    [Fact]
    public async Task ReceiveAsync_CreateSkuForFreeText_PostsStockOnReceive()
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
                    new PurchaseOrderLine
                    {
                        Description = "Custom bracket plate",
                        Quantity = 4,
                        UnitPrice = 120m,
                        Unit = "ea"
                    }
                }
            });

            await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
            var grv = await service.ReceiveAsync(
                poId,
                TestUserId,
                createSkuForFreeTextLines: true);

            Assert.NotNull(grv);
            var po = await service.GetByIdAsync(poId);
            var line = po!.Lines.Single();
            Assert.NotNull(line.InventoryItemId);

            var item = await inventory.GetItemByIdAsync(line.InventoryItemId!.Value);
            Assert.Equal(4m, item!.QuantityOnHand);
            Assert.Equal("Custom bracket plate", item.Name);
            Assert.Equal("Non-catalog", item.Category);

            var txs = await inventory.GetRecentTransactionsAsync(5);
            Assert.Contains(txs, t => t.InventoryItemId == item.Id && t.Quantity == 4m);
        }
    }

    [Fact]
    public async Task CreateFromRequisitionAsync_ThrowsWhenJobClosed()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var service = new PurchaseOrderService(db, inventory, requisitions);

        var customer = new Customer { TenantId = tenantId, Name = "Cust" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Job", QuotedTotal = 1000m };
        db.Set<Job>().Add(job);
        var supplier = new Supplier { TenantId = tenantId, Name = "Sup", IsActive = true };
        db.Set<Supplier>().Add(supplier);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "PO-CL",
            Name = "Part",
            QuantityOnHand = 0,
            UnitCost = 10m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        await db.SaveChangesAsync();

        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        job.Status = JobStatus.Closed;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<JobClosedException>(() =>
            service.CreateFromRequisitionAsync(reqId, supplier.Id));
        Assert.Contains("closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSkuFromPoLineAsync_LinksMatchingRequisitionLine_AndCarriesReservation()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var service = new PurchaseOrderService(db, inventory, requisitions);

        var customer = new Customer { TenantId = tenantId, Name = "Cust" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Job", QuotedTotal = 1000m };
        db.Set<Job>().Add(job);
        var supplier = new Supplier { TenantId = tenantId, Name = "Sup" };
        db.Set<Supplier>().Add(supplier);
        await db.SaveChangesAsync();

        var freeText = "Special flange adapter";
        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines =
            {
                new StockRequisitionLine
                {
                    Description = freeText,
                    QuantityRequested = 2,
                    EstimatedUnitCost = 250m,
                    Unit = "ea"
                }
            }
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        var poId = await service.CreateFromRequisitionAsync(reqId, supplier.Id);
        await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
        await service.ReceiveAsync(poId, TestUserId);

        var po = await service.GetByIdAsync(poId);
        var lineId = po!.Lines.Single().Id;

        var itemId = await service.CreateSkuFromPoLineAsync(lineId, sku: "FLANGE-ADAPT");
        var item = await inventory.GetItemByIdAsync(itemId);
        Assert.Equal(2m, item!.QuantityOnHand);
        // Reserved for open requisition after GRV fulfill + promote
        Assert.True(item.QuantityReserved >= 2m);

        var req = await requisitions.GetByIdAsync(reqId);
        var reqLine = req!.Lines.Single();
        Assert.Equal(itemId, reqLine.InventoryItemId);
        Assert.False(reqLine.IsNonCatalog);
    }

    [Fact]
    public async Task UpdateStatusAsync_Sent_CreatesProcurementNotification()
    {
        var tenantId = Guid.NewGuid();
        var notifications = new Mock<ITenantNotificationService>();
        TenantNotification? captured = null;
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Callback<TenantNotification, CancellationToken>((n, _) => captured = n)
            .Returns(Task.CompletedTask);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var service = new PurchaseOrderService(db, inventory, notifications: notifications.Object);

        var supplierId = Guid.NewGuid();
        db.Set<Supplier>().Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Cable Co",
            Email = "orders@cable.demo"
        });

        var poId = await service.CreateAsync(new PurchaseOrder
        {
            SupplierId = supplierId,
            TaxRate = 0m,
            Lines = { new PurchaseOrderLine { Description = "Cable", Quantity = 1, UnitPrice = 10m } }
        });

        await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

        Assert.NotNull(captured);
        Assert.Equal("PO sent to supplier", captured!.Title);
        Assert.True(
            captured.Message.Contains("orders@cable.demo", StringComparison.OrdinalIgnoreCase)
            || captured.Message.Contains("SMTP not configured", StringComparison.OrdinalIgnoreCase),
            captured.Message);
        Assert.Equal("procurement", captured.Category);
    }

    [Fact]
    public async Task UpdateStatusAsync_Sent_EmailsSupplier_WhenSmtpConfigured()
    {
        var tenantId = Guid.NewGuid();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.IsConfigured).Returns(true);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var service = new PurchaseOrderService(db, inventory, email: email.Object);

        var supplierId = Guid.NewGuid();
        db.Set<Supplier>().Add(new Supplier
        {
            Id = supplierId,
            TenantId = tenantId,
            Name = "Cable Co",
            Email = "po@cable.demo"
        });

        var poId = await service.CreateAsync(new PurchaseOrder
        {
            SupplierId = supplierId,
            TaxRate = 0m,
            Lines = { new PurchaseOrderLine { Description = "Cable 4mm", Quantity = 2, UnitPrice = 50m, Unit = "m" } }
        });

        await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

        email.Verify(e => e.SendEmailAsync(
            "po@cable.demo",
            It.Is<string>(s => s.Contains("Purchase Order")),
            It.Is<string>(b => b.Contains("Cable 4mm")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}