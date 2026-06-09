namespace METERP.Domain;

/// <summary>
/// Line on a Purchase Order (references InventoryItem for stock receipt).
/// </summary>
public class PurchaseOrderLine : BaseEntity
{
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    public string? Unit { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
