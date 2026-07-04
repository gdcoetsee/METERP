using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: verifies tenant notifications fire from key business events.
/// </summary>
public class EventNotificationTriggerTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    [Fact]
    public async Task FieldReportService_SubmitAsync_CreatesFieldNotification()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var notifications = new Mock<ITenantNotificationService>();
        TenantNotification? captured = null;
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Callback<TenantNotification, CancellationToken>((n, _) => captured = n)
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notif-field-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Site Co" });
        var jobs = new JobService(db);
        var jobId = await jobs.CreateAsync(new Job { CustomerId = customerId, Title = "Install", QuotedTotal = 5000m });

        var service = new FieldReportService(db, jobs, notifications: notifications.Object);
        await service.SubmitAsync(new FieldReport
        {
            JobId = jobId,
            SubmittedByUserId = TestUserId,
            HoursWorked = 4m,
            TravelCost = 320m
        });

        Assert.NotNull(captured);
        Assert.Equal("Field report submitted", captured!.Title);
        Assert.Equal("field", captured.Category);
        Assert.Contains('h', captured.Message);
        Assert.Contains("320", captured.Message);
        Assert.Matches(@"\d+[,.]0h", captured.Message);
        Assert.Equal("Admin,Executive,Division Manager", captured.TargetRoles);
    }

    [Fact]
    public async Task PurchaseOrderService_CreateFromRequisitionAsync_CreatesProcurementNotification()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(TestUserId);

        var notifications = new Mock<ITenantNotificationService>();
        TenantNotification? captured = null;
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Callback<TenantNotification, CancellationToken>((n, _) => captured = n)
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"notif-po-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
        var inventory = new InventoryService(db);
        var requisitions = new StockRequisitionService(db, inventory);
        var poService = new PurchaseOrderService(db, inventory, requisitions, notifications: notifications.Object);

        var customer = new Customer { TenantId = tenantId, Name = "Procure Co" };
        db.Set<Customer>().Add(customer);
        var job = new Job { TenantId = tenantId, CustomerId = customer.Id, Title = "Job", QuotedTotal = 3000m };
        db.Set<Job>().Add(job);
        var item = new InventoryItem
        {
            TenantId = tenantId,
            Sku = "OUT-001",
            Name = "Fuse",
            QuantityOnHand = 0m,
            UnitCost = 25m,
            IsActive = true
        };
        db.Set<InventoryItem>().Add(item);
        var supplier = new Supplier { TenantId = tenantId, Name = "Supplier One" };
        db.Set<Supplier>().Add(supplier);
        await db.SaveChangesAsync();

        var reqId = await requisitions.SubmitAsync(new StockRequisition
        {
            TenantId = tenantId,
            JobId = job.Id,
            RequestedByUserId = TestUserId,
            Lines = [new StockRequisitionLine { InventoryItemId = item.Id, QuantityRequested = 6 }]
        });
        await requisitions.ApproveManagerAsync(reqId, TestUserId);
        await requisitions.ApproveExecutiveAsync(reqId, TestUserId);

        var reqBefore = await requisitions.GetByIdAsync(reqId);
        Assert.Equal(RequisitionStatus.AwaitingProcurement, reqBefore!.Status);

        await poService.CreateFromRequisitionAsync(reqId, supplier.Id);

        Assert.NotNull(captured);
        Assert.Equal("Procurement PO created", captured!.Title);
        Assert.Equal("procurement", captured.Category);
        Assert.Contains("REQ-", captured.Message);
        Assert.Contains("GRV", captured.Message);
    }
}