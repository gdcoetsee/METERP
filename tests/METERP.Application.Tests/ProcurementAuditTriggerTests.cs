using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: audit log entries for sensitive procurement / requisition actions (real AuditService).
/// </summary>
public class ProcurementAuditTriggerTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public AuditService Audit { get; }
        public StockRequisitionService Requisitions { get; }
        public PurchaseOrderService PurchaseOrders { get; }
        public InventoryService Inventory { get; }

        public Harness()
        {
            TenantId = Guid.NewGuid();
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(TenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(TestUserId);
            currentUser.Setup(u => u.UserName).Returns("procurement-audit@test");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"proc-audit-{Guid.NewGuid():N}")
                .Options;

            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
            Audit = new AuditService(Db, currentUser.Object);
            Inventory = new InventoryService(Db);
            Requisitions = new StockRequisitionService(Db, Inventory, audit: Audit);
            PurchaseOrders = new PurchaseOrderService(Db, Inventory, Requisitions, audit: Audit);
        }

        public async Task<(Job Job, InventoryItem Item, Supplier Supplier)> SeedShortfallAsync()
        {
            var customer = new Customer { TenantId = TenantId, Name = "Audit Mine" };
            Db.Set<Customer>().Add(customer);
            var job = new Job { TenantId = TenantId, CustomerId = customer.Id, Title = "Panel job", QuotedTotal = 8000m };
            Db.Set<Job>().Add(job);
            var item = new InventoryItem
            {
                TenantId = TenantId,
                Sku = "AUD-001",
                Name = "Breaker",
                QuantityOnHand = 0m,
                UnitCost = 90m,
                IsActive = true
            };
            Db.Set<InventoryItem>().Add(item);
            var supplier = new Supplier { TenantId = TenantId, Name = "Audit Supplier" };
            Db.Set<Supplier>().Add(supplier);
            await Db.SaveChangesAsync();
            return (job, item, supplier);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task RequisitionApprovalChain_LogsSubmitApproveRejectActions()
    {
        using var harness = new Harness();
        var (job, item, _) = await harness.SeedShortfallAsync();

        var reqId = await harness.Requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = harness.TenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 3 }]
        });

        await harness.Requisitions.ApproveManagerAsync(reqId, TestUserId);
        await harness.Requisitions.ApproveExecutiveAsync(reqId, TestUserId);
        await harness.Requisitions.RejectAsync(reqId, TestUserId, "Project cancelled");

        var req = await harness.Requisitions.GetByIdAsync(reqId);
        Assert.NotNull(req);

        var entries = await harness.Audit.SearchAsync(entityType: "StockRequisition");
        Assert.Contains(entries, e => e.Action == "SUBMIT" && e.EntityReference == req!.RequisitionNumber);
        Assert.Contains(entries, e => e.Action == "APPROVE_MANAGER" && e.EntityReference == req.RequisitionNumber);
        Assert.Contains(entries, e => e.Action == "APPROVE_EXECUTIVE" && e.EntityReference == req.RequisitionNumber);
        Assert.Contains(entries, e => e.Action == "REJECT" && e.Details.Contains("Project cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequisitionIssue_LogsIssueAction()
    {
        using var harness = new Harness();
        var (job, item, _) = await harness.SeedShortfallAsync();

        // Seed stock so executive approval reserves and issue can proceed.
        item.QuantityOnHand = 5m;
        await harness.Db.SaveChangesAsync();

        var reqId = await harness.Requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = harness.TenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 2 }]
        });
        await harness.Requisitions.ApproveManagerAsync(reqId, TestUserId);
        await harness.Requisitions.ApproveExecutiveAsync(reqId, TestUserId);
        Assert.True(await harness.Requisitions.IssueAsync(reqId, TestUserId));

        var req = await harness.Requisitions.GetByIdAsync(reqId);
        var entries = await harness.Audit.SearchAsync(entityType: "StockRequisition", entityReference: req!.RequisitionNumber);
        Assert.Contains(entries, e => e.Action == "ISSUE" && e.Details.Contains("Stock issued", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcurementSpine_LogsPoCreateStatusReceiveAndPoReceived()
    {
        using var harness = new Harness();
        var (job, item, supplier) = await harness.SeedShortfallAsync();

        var reqId = await harness.Requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = harness.TenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 4 }]
        });
        await harness.Requisitions.ApproveManagerAsync(reqId, TestUserId);
        await harness.Requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        var poId = await harness.PurchaseOrders.CreateFromRequisitionAsync(reqId, supplier.Id);
        var po = await harness.PurchaseOrders.GetByIdAsync(poId);
        Assert.NotNull(po);

        await harness.PurchaseOrders.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
        var grv = await harness.PurchaseOrders.ReceiveAsync(poId, TestUserId);
        Assert.NotNull(grv);

        var poEntries = await harness.Audit.SearchAsync(entityType: "PurchaseOrder", entityReference: po!.PoNumber);
        Assert.Contains(poEntries, e => e.Action == "CREATE_FROM_REQ" && e.Details.Contains("REQ-", StringComparison.Ordinal));
        Assert.Contains(poEntries, e => e.Action == "STATUS" && e.Details.Contains("Sent", StringComparison.Ordinal));

        var grvEntries = await harness.Audit.SearchAsync(entityType: "GRV", entityReference: grv!.GrvNumber);
        Assert.Contains(grvEntries, e => e.Action == "RECEIVE" && e.Details.Contains(po.PoNumber, StringComparison.Ordinal));

        var req = await harness.Requisitions.GetByIdAsync(reqId);
        var reqEntries = await harness.Audit.SearchAsync(entityType: "StockRequisition", entityReference: req!.RequisitionNumber);
        Assert.Contains(reqEntries, e => e.Action == "PO_RECEIVED" && e.Details.Contains("ready for issue", StringComparison.OrdinalIgnoreCase));
    }
}