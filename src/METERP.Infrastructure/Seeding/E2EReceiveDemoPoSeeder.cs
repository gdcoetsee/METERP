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

        var demoPos = (await purchaseOrderService.GetAllAsync(pageSize: 200, ct: ct))
            .Where(p => p.Notes != null
                        && p.Notes.Contains(DemoNotesMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var candidate in demoPos.Where(p => p.Status == PurchaseOrderStatus.Sent))
        {
            var full = await purchaseOrderService.GetByIdAsync(candidate.Id, ct);
            if (full?.Lines.Any(l => !l.IsDeleted) == true)
                return;
        }

        foreach (var stale in demoPos)
        {
            try
            {
                if (stale.Status == PurchaseOrderStatus.Sent)
                    await purchaseOrderService.UpdateStatusAsync(stale.Id, PurchaseOrderStatus.Cancelled, ct);

                if (stale.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled)
                    await purchaseOrderService.DeleteAsync(stale.Id, ct);
            }
            catch (InvalidOperationException)
            {
                // Received / locked rows stay — never crash host startup.
            }
        }

        var panelSupplier = (await supplierService.GetAllAsync(pageSize: 200, ct: ct))
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
            Status = PurchaseOrderStatus.Draft,
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

        await purchaseOrderService.UpdateStatusAsync(sentPoId, PurchaseOrderStatus.Sent, ct);
    }
}