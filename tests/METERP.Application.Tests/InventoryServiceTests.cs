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
    private AppDbContext CreateContext(Guid? fixedTenantId = null)
    {
        var tenantId = fixedTenantId ?? Guid.NewGuid();
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
    public async Task CreateItemAsync_ThrowsWhenUnitCostTooHigh()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateItemAsync(new InventoryItem
            {
                Name = "Gold plate",
                QuantityOnHand = 1,
                UnitCost = 1_000_001m
            }));
        Assert.Contains("1,000,000", ex.Message);
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
    public async Task UpdateItemAsync_PersistsMasterData_ButNotQuantityOnHand()
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
        // Direct qty edits via Update are ignored — stock must move via transactions.
        item!.QuantityOnHand = 20;
        item.ReorderLevel = 8;
        item.Name = "Cable drum 500m";
        item.UnitCost = 90m;

        await service.UpdateItemAsync(item);

        var reloaded = await service.GetItemByIdAsync(id);
        Assert.NotNull(reloaded);
        Assert.Equal(12, reloaded!.QuantityOnHand);
        Assert.Equal(8, reloaded.ReorderLevel);
        Assert.Equal("Cable drum 500m", reloaded.Name);
        Assert.Equal(90m, reloaded.UnitCost);
    }

    [Fact]
    public async Task CreateItemAsync_ThrowsWhenReorderLevelNegative()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateItemAsync(new InventoryItem
            {
                Sku = "NEG-RO",
                Name = "Bad reorder",
                ReorderLevel = -1,
                UnitCost = 1m
            }));
    }

    [Fact]
    public async Task UpdateItemAsync_PreservesSku()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "sku-lock",
            Name = "Fuse",
            QuantityOnHand = 5,
            UnitCost = 2m
        });

        var item = await service.GetItemByIdAsync(id);
        Assert.NotNull(item);
        item!.Sku = "HACKED";
        item.Name = "Fuse 10A";
        await service.UpdateItemAsync(item);

        var reloaded = await service.GetItemByIdAsync(id);
        Assert.Equal("SKU-LOCK", reloaded!.Sku);
        Assert.Equal("Fuse 10A", reloaded.Name);
    }

    [Fact]
    public async Task UpdateItemAsync_ThrowsWhenDeactivatingItemOnOpenRequisition()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "REQ-OPEN",
            Name = "Cable",
            QuantityOnHand = 20,
            ReorderLevel = 2,
            UnitCost = 10m,
            IsActive = true
        });

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-REQ",
            Title = "Work",
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        var req = new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequisitionNumber = "REQ-1",
            Status = RequisitionStatus.PendingManager
        };
        db.Set<StockRequisition>().Add(req);
        db.Set<StockRequisitionLine>().Add(new StockRequisitionLine
        {
            TenantId = tenantId,
            StockRequisitionId = req.Id,
            InventoryItemId = id,
            QuantityRequested = 2,
            Description = "Cable"
        });
        await db.SaveChangesAsync();

        var item = await service.GetItemByIdAsync(id);
        item!.IsActive = false;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateItemAsync(item));
        Assert.Contains("requisition", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateItemAsync_ThrowsWhenDeactivatingItemWithReservedStock()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "RSV-001",
            Name = "Reserved cable",
            QuantityOnHand = 20,
            QuantityReserved = 5,
            IsActive = true
        });

        // QuantityReserved is preserved from store; seed reserved via direct update after create.
        var tracked = await db.Set<InventoryItem>().FirstAsync(i => i.Id == id);
        tracked.QuantityReserved = 5;
        await db.SaveChangesAsync();

        var item = await service.GetItemByIdAsync(id);
        Assert.NotNull(item);
        item!.IsActive = false;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateItemAsync(item));
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task RecordStockTransactionAsync_ThrowsWhenItemMissing()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordStockTransactionAsync(Guid.NewGuid(), -5, StockTransactionType.Issue));
    }

    [Fact]
    public async Task RecordStockTransactionAsync_ThrowsWhenQuantityZero()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "ZERO",
            Name = "Zero",
            QuantityOnHand = 5,
            ReorderLevel = 1,
            UnitCost = 1m
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordStockTransactionAsync(id, 0, StockTransactionType.Adjustment));
        Assert.Contains("zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_ThrowsWhenItemInactive()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "INACT",
            Name = "Inactive",
            QuantityOnHand = 5,
            ReorderLevel = 1,
            UnitCost = 1m,
            IsActive = true
        });
        var item = await service.GetItemByIdAsync(id);
        item!.IsActive = false;
        await service.UpdateItemAsync(item);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordStockTransactionAsync(id, -1, StockTransactionType.Issue));
        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_ThrowsWhenLinkedJobMissing()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "JOB-MISS",
            Name = "Cable",
            QuantityOnHand = 20,
            ReorderLevel = 2,
            UnitCost = 10m
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordStockTransactionAsync(id, -1, StockTransactionType.Issue, jobId: Guid.NewGuid()));
        Assert.Contains("job", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_ThrowsWhenIssuingToClosedJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "JOB-CLOSED",
            Name = "Fittings",
            QuantityOnHand = 20,
            ReorderLevel = 2,
            UnitCost = 5m
        });

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-CLOSED-STK",
            Title = "Done",
            Status = JobStatus.Closed
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordStockTransactionAsync(id, -1, StockTransactionType.Issue, jobId: job.Id));
        Assert.Contains("Closed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_AllowsIssueToOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new InventoryService(db);
        var id = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "JOB-OPEN",
            Name = "Conduit",
            QuantityOnHand = 20,
            ReorderLevel = 2,
            UnitCost = 8m
        });

        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-OPEN-STK",
            Title = "Live",
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        await service.RecordStockTransactionAsync(id, -2, StockTransactionType.Issue, reference: job.JobNumber, jobId: job.Id);

        var item = await service.GetItemByIdAsync(id);
        Assert.Equal(18m, item!.QuantityOnHand);
    }

    [Fact]
    public async Task GetRecentTransactionsAsync_ReturnsNewestFirstAcrossItems()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        var id1 = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "TXN-A",
            Name = "Item A",
            QuantityOnHand = 10,
            ReorderLevel = 2,
            UnitCost = 10m
        });
        var id2 = await service.CreateItemAsync(new InventoryItem
        {
            Sku = "TXN-B",
            Name = "Item B",
            QuantityOnHand = 5,
            ReorderLevel = 1,
            UnitCost = 20m
        });

        await service.RecordStockTransactionAsync(id1, 1, StockTransactionType.Receipt, notes: "First");
        await service.RecordStockTransactionAsync(id2, 2, StockTransactionType.Receipt, notes: "Second");

        var recent = await service.GetRecentTransactionsAsync(take: 5);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Second", recent[0].Notes);
        Assert.Equal("First", recent[1].Notes);
    }

    [Fact]
    public async Task GetAllItemsAsync_FiltersBySearchTerm()
    {
        using var db = CreateContext();
        var service = new InventoryService(db);
        await service.CreateItemAsync(new InventoryItem
        {
            Sku = "CABLE-4MM",
            Name = "SWA Cable 50m",
            QuantityOnHand = 10,
            ReorderLevel = 2,
            Category = "Electrical"
        });
        await service.CreateItemAsync(new InventoryItem
        {
            Sku = "FUSE-32A",
            Name = "DIN Fuse 32A",
            QuantityOnHand = 50,
            ReorderLevel = 10,
            Category = "Electrical"
        });

        var results = await service.GetAllItemsAsync(search: "cable");

        Assert.Single(results);
        Assert.Equal("CABLE-4MM", results[0].Sku);
    }
}