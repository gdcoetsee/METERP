using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// GRV register keeps the supplier delivery note. DN-1 stays DN-1. A blank note is a dash.
/// Another tenant and a deleted receipt stay off the page.
/// </summary>
public class GrvRegisterDeliveryNoteTests
{
    [Fact]
    public void Show_KeepsDn1_AndUsesADashWhenTheNoteIsMissing()
    {
        Assert.Equal("\u2014", GrvDeliveryNote.Blank);
        Assert.Equal("DN-1", GrvDeliveryNote.Show("DN-1"));
        Assert.Equal("DN-1", GrvDeliveryNote.Show("  DN-1  "));
        Assert.Equal(GrvDeliveryNote.Blank, GrvDeliveryNote.Show(null));
        Assert.Equal(GrvDeliveryNote.Blank, GrvDeliveryNote.Show(""));
        Assert.Equal(GrvDeliveryNote.Blank, GrvDeliveryNote.Show("   "));
    }

    [Fact]
    public async Task GetGrvsPageAsync_ReturnsSavedDeliveryNote_AndHidesBlankOtherTenantAndDeleted()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var other = CreateContext(dbName, tenantB, userId))
        {
            await ReceiveAsync(other, tenantB, userId, "Other Stores", "DN-OTHER");
        }

        await using var db = CreateContext(dbName, tenantA, userId);
        var noted = await ReceiveAsync(db, tenantA, userId, "Cable Co", " DN-1 ");
        var blank = await ReceiveAsync(db, tenantA, userId, "Cable Co", null);
        var whitespace = await ReceiveAsync(db, tenantA, userId, "Cable Co", "   ");
        var deleted = await ReceiveAsync(db, tenantA, userId, "Cable Co", "DN-HIDDEN");
        deleted.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.Equal("DN-1", noted.SupplierDeliveryNote);
        Assert.Null(blank.SupplierDeliveryNote);
        Assert.Null(whitespace.SupplierDeliveryNote);

        var page = await new PurchaseOrderService(db, new InventoryService(db)).GetGrvsPageAsync();

        Assert.Equal(3, page.TotalCount);
        Assert.Contains(page.Items, g => g.GrvNumber == noted.GrvNumber && g.SupplierDeliveryNote == "DN-1");
        Assert.Contains(page.Items, g => g.GrvNumber == blank.GrvNumber && g.SupplierDeliveryNote == null);
        Assert.Contains(page.Items, g => g.GrvNumber == whitespace.GrvNumber && g.SupplierDeliveryNote == null);
        Assert.DoesNotContain(page.Items, g => g.SupplierDeliveryNote is "DN-OTHER" or "DN-HIDDEN");
        Assert.Equal("DN-1", GrvDeliveryNote.Show(page.Items.Single(g => g.GrvNumber == noted.GrvNumber).SupplierDeliveryNote));
        Assert.Equal(GrvDeliveryNote.Blank, GrvDeliveryNote.Show(page.Items.Single(g => g.GrvNumber == blank.GrvNumber).SupplierDeliveryNote));
        Assert.Equal(GrvDeliveryNote.Blank, GrvDeliveryNote.Show(page.Items.Single(g => g.GrvNumber == whitespace.GrvNumber).SupplierDeliveryNote));

        var notedRow = page.Items.Single(g => g.GrvNumber == noted.GrvNumber);
        Assert.Equal("PO-DN-1", notedRow.PurchaseOrder?.PoNumber);
        Assert.Equal("Cable Co", notedRow.PurchaseOrder?.Supplier?.Name);
    }

    private static AppDbContext CreateContext(string dbName, Guid tenantId, Guid userId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(userId);
        currentUser.Setup(u => u.UserName).Returns("grv-register");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private static async Task<GoodsReceiptVoucher> ReceiveAsync(
        AppDbContext db,
        Guid tenantId,
        Guid userId,
        string supplierName,
        string? deliveryNote)
    {
        var supplier = await db.Set<Supplier>().FirstOrDefaultAsync(s => s.Name == supplierName);
        if (supplier == null)
        {
            supplier = new Supplier { TenantId = tenantId, Name = supplierName, IsActive = true };
            db.Set<Supplier>().Add(supplier);
            await db.SaveChangesAsync();
        }

        var service = new PurchaseOrderService(db, new InventoryService(db));
        var poNumber = deliveryNote switch
        {
            " DN-1 " => "PO-DN-1",
            null => "PO-BLANK",
            "   " => "PO-SPACE",
            "DN-HIDDEN" => "PO-HIDDEN",
            "DN-OTHER" => "PO-OTHER",
            _ => "PO-" + Guid.NewGuid().ToString("N")[..6]
        };
        var poId = await service.CreateAsync(new PurchaseOrder
        {
            SupplierId = supplier.Id,
            PoNumber = poNumber,
            TaxRate = 0m,
            Lines =
            {
                new PurchaseOrderLine { Description = "11kV bushing", Quantity = 2m, UnitPrice = 10m }
            }
        });
        await service.UpdateStatusAsync(poId, PurchaseOrderStatus.Sent);
        var grv = await service.ReceiveAsync(poId, userId, deliveryNote);
        return grv ?? throw new InvalidOperationException("Expected a GRV.");
    }
}
