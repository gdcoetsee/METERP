namespace METERP.Domain;

/// <summary>
/// Requisition line: either a catalog stock item or a free-text / non-catalog need.
/// </summary>
public class StockRequisitionLine : BaseEntity
{
    public Guid StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    /// <summary>Null when the line is free-text (item not yet in stock master).</summary>
    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>Required for non-catalog lines; optional note for catalog lines.</summary>
    public string Description { get; set; } = string.Empty;

    public string? Unit { get; set; }

    /// <summary>Estimated unit cost for non-catalog procurement / job costing.</summary>
    public decimal EstimatedUnitCost { get; set; }

    public decimal QuantityRequested { get; set; }

    public decimal QuantityReserved { get; set; }

    public decimal QuantityIssued { get; set; }

    public bool IsNonCatalog => !InventoryItemId.HasValue || InventoryItemId == Guid.Empty;

    public string DisplayDescription =>
        !string.IsNullOrWhiteSpace(Description)
            ? Description.Trim()
            : InventoryItem?.Name ?? "Material";
}
