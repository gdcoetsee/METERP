using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: procurement spine — shortfall requisition → PO → GRV → stock reserved for job issue.
/// </summary>
public class ProcurementSpineFlowTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    [Fact]
    public async Task ShortfallRequisition_ToPoReceive_ReservesStockForJobIssue()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"proc-spine-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var poService = new PurchaseOrderService(db, inventory, requisitions);

        var customer = new Customer { TenantId = tenantId, Name = "Mining Co" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Panel upgrade", QuotedTotal = 12000m };
        db.Set<Job>().Add(job);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "BRK-100",
            Name = "Breaker",
            QuantityOnHand = 0m,
            UnitCost = 80m,
            ReorderLevel = 2m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        var supplier = new Supplier { TenantId = tenantId, Name = "Electrical Supplies" };
        db.Set<Supplier>().Add(supplier);
        await db.SaveChangesAsync();

        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 5 }]
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        var poId = await poService.CreateFromRequisitionAsync(reqId, supplier.Id);
        await poService.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);

        var onHandBeforeReceive = (await inventory.GetItemByIdAsync(item.Id))!.QuantityOnHand;
        Assert.Equal(0m, onHandBeforeReceive);

        var grv = await poService.ReceiveAsync(poId, TestUserId);
        Assert.NotNull(grv);

        var onHandAfterReceive = (await inventory.GetItemByIdAsync(item.Id))!.QuantityOnHand;
        Assert.Equal(5m, onHandAfterReceive);

        var reqAfterReceive = await requisitions.GetByIdAsync(reqId);
        Assert.NotNull(reqAfterReceive);
        Assert.Equal(RequisitionStatus.Approved, reqAfterReceive!.Status);
        Assert.Equal(5m, reqAfterReceive.Lines.First().QuantityReserved);

        Assert.True(await requisitions.IssueAsync(reqId, TestUserId));

        var onHandAfterIssue = (await inventory.GetItemByIdAsync(item.Id))!.QuantityOnHand;
        Assert.Equal(0m, onHandAfterIssue);

        var materialCost = await db.Set<JobCost>().FirstAsync(c => c.JobId == job.Id);
        Assert.Equal("Material", materialCost.CostType);
        Assert.Equal(400m, materialCost.Amount);
    }
}