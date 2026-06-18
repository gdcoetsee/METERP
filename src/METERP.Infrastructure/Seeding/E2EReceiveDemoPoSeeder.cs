using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Idempotent Sent PO for receive E2E (Panel Supplies → LED-HB-150 qty 3).
/// Soft-deletes all prior receive-demo POs and creates a fresh Sent PO.
/// </summary>
public static class E2EReceiveDemoPoSeeder
{
    public const string DemoNotesMarker = "E2E receive demo";

    public static async Task EnsureSentReceiveDemoPoAsync(
        IPurchaseOrderService purchaseOrderService,
        ISupplierService supplierService,
        IInventoryService inventoryService,
        ITenantProvider tenantProvider,
        Guid tenantId,
        CancellationToken ct = default)
    {
        tenantProvider.SetTenantId(tenantId);

        var demoPos = await purchaseOrderService.GetAllAsync(ct: ct);
        foreach (var stale in demoPos.Where(p =>
                     p.Notes != null
                     && p.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase)))
        {
            await purchaseOrderService.DeleteAsync(stale.Id, ct);
        }

        var panelSupplier = (await supplierService.GetAllAsync(ct: ct))
            .FirstOrDefault(s => s.Name.Contains("Panel Supplies", StringComparison.OrdinalIgnoreCase));
        var ledItem = (await inventoryService.GetAllItemsAsync(ct: ct))
            .FirstOrDefault(i => i.Sku == "LED-HB-150");
        if (panelSupplier == null || ledItem == null)
            return;

        var sentPoId = await purchaseOrderService.CreateAsync(new PurchaseOrder
        {
            SupplierId = panelSupplier.Id,
            PoDate = DateTime.UtcNow.AddDays(-2),
            ExpectedDate = DateTime.UtcNow.AddDays(2),
            Status = PurchaseOrderStatus.Sent,
            TaxRate = 0.15m,
            Notes = "E2E receive demo PO"
        }, ct);

        await purchaseOrderService.AddLineAsync(new PurchaseOrderLine
        {
            PurchaseOrderId = sentPoId,
            InventoryItemId = ledItem.Id,
            Description = ledItem.Name,
            Quantity = 3,
            UnitPrice = ledItem.UnitCost,
            Unit = ledItem.Unit
        }, ct);
    }
}