using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ProcurementQuoteServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private static async Task<(AppDbContext Db, ProcurementQuoteService Quotes, PurchaseOrderService Pos, Guid TenantId, Guid ReqId, Guid SupplierA, Guid SupplierB)> SeedAwaitingProcurementAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rfq-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var pos = new PurchaseOrderService(db, inventory, requisitions);
        var quotes = new ProcurementQuoteService(db, pos);

        var customer = new Customer { TenantId = tenantId, Name = "RFQ Customer" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "RFQ job", QuotedTotal = 5000m };
        db.Set<Job>().Add(job);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "RFQ-SKU",
            Name = "RFQ part",
            QuantityOnHand = 0m,
            UnitCost = 50m,
            ReorderLevel = 1m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        var supplierA = new Supplier { TenantId = tenantId, Name = "Supplier A" };
        var supplierB = new Supplier { TenantId = tenantId, Name = "Supplier B" };
        db.Set<Supplier>().AddRange(supplierA, supplierB);
        await db.SaveChangesAsync();

        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 3 }]
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        return (db, quotes, pos, tenantId, reqId, supplierA.Id, supplierB.Id);
    }

    [Fact]
    public async Task AddQuote_Select_CreatePo_UsesSelectedSupplier()
    {
        var (db, quotes, pos, _, reqId, supplierA, supplierB) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            await quotes.AddQuoteAsync(reqId, supplierA, 900m, "Higher");
            var cheapId = await quotes.AddQuoteAsync(reqId, supplierB, 700m, "Winner");

            var list = await quotes.GetForRequisitionAsync(reqId);
            Assert.Equal(2, list.Count);
            Assert.Equal(700m, list[0].QuotedTotal); // ordered by total

            Assert.True(await quotes.SelectQuoteAsync(cheapId, TestUserId));
            list = await quotes.GetForRequisitionAsync(reqId);
            var selected = Assert.Single(list, q => q.IsSelected);
            Assert.Equal(supplierB, selected.SupplierId);

            var poId = await quotes.CreatePoFromSelectedQuoteAsync(reqId);
            var po = await pos.GetByIdAsync(poId);
            Assert.NotNull(po);
            Assert.Equal(supplierB, po!.SupplierId);
            Assert.Equal(PurchaseOrderStatus.Draft, po.Status);
        }
    }

    [Fact]
    public async Task CreatePoFromSelectedQuote_WithoutSelection_Throws()
    {
        var (db, quotes, _, _, reqId, supplierA, _) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            await quotes.AddQuoteAsync(reqId, supplierA, 100m);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                quotes.CreatePoFromSelectedQuoteAsync(reqId));
        }
    }

    [Fact]
    public async Task AddQuote_WhenNotAwaitingProcurement_Throws()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rfq-bad-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var pos = new PurchaseOrderService(db, inventory);
        var quotes = new ProcurementQuoteService(db, pos);

        var customer = new Customer { TenantId = tenantId, Name = "C" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "J", QuotedTotal = 1m };
        db.Set<Job>().Add(job);
        var supplier = new Supplier { TenantId = tenantId, Name = "S" };
        db.Set<Supplier>().Add(supplier);
        var req = new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Status = RequisitionStatus.PendingManager,
            RequisitionNumber = "REQ-TEST"
        };
        db.Set<StockRequisition>().Add(req);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            quotes.AddQuoteAsync(req.Id, supplier.Id, 50m));
    }
}
