namespace METERP.Domain;

/// <summary>
/// Stock item / material that can be used in Quotes and Jobs.
/// Core of the Inventory & Stock Transactions module.
/// </summary>
public class InventoryItem : BaseEntity
{
    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Unit { get; set; } = "ea"; // ea, m, kg, hr, etc.

    /// <summary>
    /// Current quantity on hand for this tenant.
    /// </summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>
    /// Reorder / minimum level alert threshold.
    /// </summary>
    public decimal ReorderLevel { get; set; } = 0;

    public decimal UnitCost { get; set; }

    public string? Category { get; set; } // e.g. "Electrical", "Mechanical", "Consumables"

    public bool IsActive { get; set; } = true;
}
