using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// PO register of lines still to receive. Fully received and cancelled orders stay off it.
/// </summary>
public class PurchaseOrderOutstandingRegisterTests
{
    private static AppDbContext CreateContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.UserName).Returns("po-register");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private static PurchaseOrderService ServiceFor(AppDbContext db) =>
        new(db, new InventoryService(db));

    private static async Task<Supplier> SeedSupplierAsync(AppDbContext db, Guid tenantId, string name)
    {
        var supplier = new Supplier { TenantId = tenantId, Name = name, IsActive = true };
        db.Set<Supplier>().Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    private static PurchaseOrder Order(
        Guid tenantId,
        Guid supplierId,
        string number,
        PurchaseOrderStatus status,
        DateTime poDate,
        params PurchaseOrderLine[] lines)
    {
        var po = new PurchaseOrder
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            PoNumber = number,
            Status = status,
            PoDate = poDate,
            TaxRate = 0.15m
        };
        foreach (var line in lines)
        {
            line.TenantId = tenantId;
            po.Lines.Add(line);
        }

        return po;
    }

    private static PurchaseOrderLine Line(string description, decimal ordered, decimal received, decimal unitPrice = 10m) =>
        new()
        {
            Description = description,
            Quantity = ordered,
            QuantityReceived = received,
            UnitPrice = unitPrice
        };

    [Fact]
    public async Task GetOutstandingRegisterAsync_PartialLineShowsOrderedReceivedAndOutstanding_ExcludesReceivedAndCancelled()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(dbName, tenantId);
        var supplier = await SeedSupplierAsync(db, tenantId, "Cable Co");
        var day = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var deletedLine = Line("Deleted cable", 8m, 1m);
        db.Set<PurchaseOrder>().AddRange(
            Order(tenantId, supplier.Id, "PO-PART", PurchaseOrderStatus.PartiallyReceived, day,
                Line("11kV bushing", 10m, 4m),
                Line("Gasket", 5m, 5m),
                Line("Fractional tape", 10.5m, 4.25m),
                Line("Over received", 2m, 5m),
                deletedLine),
            Order(tenantId, supplier.Id, "PO-SENT", PurchaseOrderStatus.Sent, day.AddDays(-1),
                Line("Copper bar", 3m, 0m)),
            Order(tenantId, supplier.Id, "PO-DRAFT", PurchaseOrderStatus.Draft, day.AddDays(-2),
                Line("Draft lug", 1m, 0m)),
            Order(tenantId, supplier.Id, "PO-DONE", PurchaseOrderStatus.Received, day,
                Line("Still open on a received PO", 10m, 2m)),
            Order(tenantId, supplier.Id, "PO-CANCEL", PurchaseOrderStatus.Cancelled, day,
                Line("Cancelled lug", 6m, 0m)));
        await db.SaveChangesAsync();
        deletedLine.IsDeleted = true;
        await db.SaveChangesAsync();

        var rows = await ServiceFor(db).GetOutstandingRegisterAsync();

        var bushing = Assert.Single(rows, r => r.Description == "11kV bushing");
        Assert.Equal("PO-PART", bushing.PoNumber);
        Assert.Equal("Cable Co", bushing.SupplierName);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, bushing.Status);
        Assert.Equal(10m, bushing.Ordered);
        Assert.Equal(4m, bushing.Received);
        Assert.Equal(6m, bushing.Outstanding);

        var tape = Assert.Single(rows, r => r.Description == "Fractional tape");
        Assert.Equal(10.5m, tape.Ordered);
        Assert.Equal(4.25m, tape.Received);
        Assert.Equal(6.25m, tape.Outstanding);

        var copper = Assert.Single(rows, r => r.Description == "Copper bar");
        Assert.Equal(3m, copper.Ordered);
        Assert.Equal(0m, copper.Received);
        Assert.Equal(3m, copper.Outstanding);
        Assert.Equal(PurchaseOrderStatus.Sent, copper.Status);

        Assert.Contains(rows, r => r.PoNumber == "PO-DRAFT" && r.Description == "Draft lug" && r.Outstanding == 1m);
        Assert.DoesNotContain(rows, r => r.Description is "Gasket" or "Over received" or "Deleted cable");
        Assert.DoesNotContain(rows, r => r.PoNumber is "PO-DONE" or "PO-CANCEL");
        Assert.Equal(new[] { "PO-PART", "PO-PART", "PO-SENT", "PO-DRAFT" }, rows.Select(r => r.PoNumber).ToArray());
    }

    [Fact]
    public async Task GetOutstandingRegisterAsync_ExcludesOtherTenantAndSoftDeletedPurchaseOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var day = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            var supplierB = await SeedSupplierAsync(seedB, tenantB, "Other Tenant Stores");
            seedB.Set<PurchaseOrder>().Add(
                Order(tenantB, supplierB.Id, "PO-B", PurchaseOrderStatus.Sent, day,
                    Line("Tenant B cable", 4m, 1m)));
            await seedB.SaveChangesAsync();
        }

        await using var db = CreateContext(dbName, tenantA);
        var supplierA = await SeedSupplierAsync(db, tenantA, "Our Stores");
        var deletedPo = Order(tenantA, supplierA.Id, "PO-DELETED", PurchaseOrderStatus.Sent, day,
            Line("Deleted PO line", 9m, 0m));
        db.Set<PurchaseOrder>().AddRange(
            deletedPo,
            Order(tenantA, supplierA.Id, "PO-A", PurchaseOrderStatus.PartiallyReceived, day,
                Line("Our bushing", 8m, 3m)));
        await db.SaveChangesAsync();
        deletedPo.IsDeleted = true;
        await db.SaveChangesAsync();

        var rows = await ServiceFor(db).GetOutstandingRegisterAsync();

        var ours = Assert.Single(rows);
        Assert.Equal("PO-A", ours.PoNumber);
        Assert.Equal("Our Stores", ours.SupplierName);
        Assert.Equal("Our bushing", ours.Description);
        Assert.Equal(8m, ours.Ordered);
        Assert.Equal(3m, ours.Received);
        Assert.Equal(5m, ours.Outstanding);
        Assert.DoesNotContain(rows, r => r.PoNumber is "PO-B" or "PO-DELETED");
    }
}