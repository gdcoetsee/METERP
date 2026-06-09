namespace METERP.Domain;

/// <summary>
/// Line item on a Sales Order (typically copied from Quote).
/// </summary>
public class SalesOrderLine : BaseEntity
{
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    public string? Unit { get; set; }

    public string LineType { get; set; } = "Material";

    public decimal LineTotal => Quantity * UnitPrice;

    public Guid? InventoryItemId { get; set; }
}
