using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
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
    public async Task CreatePoFromSelectedQuote_ThrowsWhenSelectedTotalZero()
    {
        var (db, quotes, _, _, reqId, supplierA, _) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            // Header path normally rejects zero; force a zero total after select for the guard.
            var quoteId = await quotes.AddQuoteAsync(reqId, supplierA, 100m);
            Assert.True(await quotes.SelectQuoteAsync(quoteId, Guid.NewGuid()));
            var quote = await db.Set<ProcurementSupplierQuote>().FirstAsync(q => q.Id == quoteId);
            quote.QuotedTotal = 0m;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                quotes.CreatePoFromSelectedQuoteAsync(reqId));
            Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AddQuote_ThrowsWhenQuotedTotalZeroWithoutLines()
    {
        var (db, quotes, _, _, reqId, supplierA, _) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                quotes.AddQuoteAsync(reqId, supplierA, 0m));
        }
    }

    [Fact]
    public async Task AddQuote_ThrowsWhenSupplierInactive()
    {
        var (db, quotes, _, _, reqId, supplierA, _) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            var supplier = await db.Set<Supplier>().FirstAsync(s => s.Id == supplierA);
            supplier.IsActive = false;
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                quotes.AddQuoteAsync(reqId, supplierA, 50m));
        }
    }

    [Fact]
    public async Task AddQuote_ThrowsWhenSupplierAlreadyQuoted()
    {
        var (db, quotes, _, _, reqId, supplierA, _) = await SeedAwaitingProcurementAsync();
        await using (db)
        {
            await quotes.AddQuoteAsync(reqId, supplierA, 100m);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                quotes.AddQuoteAsync(reqId, supplierA, 90m));
            Assert.Contains("already has a quote", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task AddQuote_WithLines_ComputesTotal_AndAppliesPricesToPo()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rfq-lines-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var pos = new PurchaseOrderService(db, inventory, requisitions);
        var quotes = new ProcurementQuoteService(db, pos);

        var customer = new Customer { TenantId = tenantId, Name = "RFQ Lines Customer" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "RFQ lines job", QuotedTotal = 8000m };
        db.Set<Job>().Add(job);
        var supplier = new Supplier { TenantId = tenantId, Name = "Line Supplier" };
        db.Set<Supplier>().Add(supplier);
        await db.SaveChangesAsync();

        // Pure free-text shortfall lines → always AwaitingProcurement (no catalog reserve).
        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines =
            [
                new StockRequisitionLine
                {
                    Description = "RFQ part A",
                    QuantityRequested = 3,
                    EstimatedUnitCost = 50m,
                    Unit = "ea"
                },
                new StockRequisitionLine
                {
                    Description = "Custom gasket kit",
                    QuantityRequested = 2,
                    EstimatedUnitCost = 40m,
                    Unit = "kit"
                }
            ]
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        var req = await requisitions.GetByIdAsync(reqId);
        Assert.Equal(RequisitionStatus.AwaitingProcurement, req!.Status);
        var shortfallLines = req.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Description).ToList();
        Assert.Equal(2, shortfallLines.Count);

        var quoteId = await quotes.AddQuoteAsync(
            reqId,
            supplier.Id,
            quotedTotal: 0,
            notes: "Line detail",
            lines:
            [
                new ProcurementQuoteLineInput(
                    shortfallLines[0].Id,
                    shortfallLines[0].DisplayDescription,
                    Quantity: shortfallLines[0].QuantityRequested,
                    UnitPrice: 90m),
                new ProcurementQuoteLineInput(
                    shortfallLines[1].Id,
                    shortfallLines[1].DisplayDescription,
                    Quantity: shortfallLines[1].QuantityRequested,
                    UnitPrice: 55m)
            ]);

        var list = await quotes.GetForRequisitionAsync(reqId);
        var quote = Assert.Single(list, q => q.Id == quoteId);
        Assert.Equal(2 * 90m + 3 * 55m, quote.QuotedTotal); // 345
        Assert.Equal(2, quote.Lines.Count);

        Assert.True(await quotes.SelectQuoteAsync(quoteId, TestUserId));
        var poId = await quotes.CreatePoFromSelectedQuoteAsync(reqId);
        var po = await pos.GetByIdAsync(poId);
        Assert.NotNull(po);
        Assert.Equal(supplier.Id, po!.SupplierId);

        var gasketLine = po.Lines.FirstOrDefault(l =>
            l.Description.Contains("gasket", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(gasketLine);
        Assert.Equal(90m, gasketLine!.UnitPrice);
        Assert.Equal(2m, gasketLine.Quantity);

        var partLine = po.Lines.FirstOrDefault(l =>
            l.Description.Contains("part A", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(partLine);
        Assert.Equal(55m, partLine!.UnitPrice);
        Assert.Equal(3m, partLine.Quantity);
    }
}
