using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: inventory stock transactions linked to jobs via requisition issue (real InventoryService).
/// </summary>
public class InventoryJobUsageIntegrationTests
{
    private static (StockRequisitionService Requisitions, InventoryService Inventory, AppDbContext Db, Guid TenantId, Job Job, InventoryItem Item)
        CreateHarness(decimal onHand = 12m)
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"inv-job-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);

        var customer = new Customer { TenantId = tenantId, Name = "Field Co" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Panel install", QuotedTotal = 8000m };
        db.Set<Job>().Add(job);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "CBL-4MM",
            Name = "SWA Cable",
            QuantityOnHand = onHand,
            UnitCost = 120m,
            ReorderLevel = 2m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        db.SaveChanges();

        return (requisitions, inventory, db, tenantId, job, item);
    }

    [Fact]
    public async Task RecordStockTransactionAsync_PersistsJobIdOnTransaction()
    {
        var (_, inventory, db, _, job, item) = CreateHarness();
        await using (db)
        {
            await inventory.RecordStockTransactionAsync(
                item.Id, -2m, StockTransactionType.Issue, job.JobNumber, job.Id, "Direct job issue");

            var txns = await inventory.GetTransactionsForItemAsync(item.Id);
            var txn = Assert.Single(txns);
            Assert.Equal(job.Id, txn.JobId);
            Assert.Equal(job.JobNumber, txn.Reference);
            Assert.Equal(-2m, txn.Quantity);

            var updated = await inventory.GetItemByIdAsync(item.Id);
            Assert.Equal(10m, updated!.QuantityOnHand);
        }
    }

    [Fact]
    public async Task RequisitionIssue_DecrementsOnHand_AndLinksStockTransactionToJob()
    {
        var (requisitions, inventory, db, tenantId, job, item) = CreateHarness(onHand: 10m);
        await using (db)
        {
            var reqId = await requisitions.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 4 }]
            });

            Assert.True(await requisitions.ApproveManagerAsync(reqId, Guid.NewGuid()));
            Assert.True(await requisitions.ApproveExecutiveAsync(reqId, Guid.NewGuid()));
            Assert.True(await requisitions.IssueAsync(reqId, Guid.NewGuid()));

            var onHand = (await inventory.GetItemByIdAsync(item.Id))!.QuantityOnHand;
            Assert.Equal(6m, onHand);

            var txns = await inventory.GetTransactionsForItemAsync(item.Id);
            var issueTxn = Assert.Single(txns);
            Assert.Equal(job.Id, issueTxn.JobId);
            Assert.Equal(StockTransactionType.Issue, issueTxn.Type);
            Assert.Equal(-4m, issueTxn.Quantity);

            var materialCost = await db.Set<JobCost>().FirstAsync(c => c.JobId == job.Id);
            Assert.Equal("Material", materialCost.CostType);
            Assert.Equal(480m, materialCost.Amount);
        }
    }

    [Fact]
    public async Task GetByJobIdAsync_ReturnsRequisitionsForJob()
    {
        var (requisitions, _, db, tenantId, job, item) = CreateHarness();
        await using (db)
        {
            await requisitions.SubmitAsync(new StockRequisition
            {
                TenantId = tenantId,
                JobId = job.Id,
                RequestedByUserId = Guid.NewGuid(),
                Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 1 }]
            });

            var forJob = await requisitions.GetByJobIdAsync(job.Id);
            Assert.Single(forJob);
            Assert.Equal(job.Id, forJob[0].JobId);
        }
    }
}