namespace METERP.Domain;

/// <summary>
/// Audit trail for every stock movement (in, out, adjustment, job usage).
/// </summary>
public class StockTransaction : BaseEntity
{
    public Guid InventoryItemId { get; set; }
    public InventoryItem InventoryItem { get; set; } = null!;

    public StockTransactionType Type { get; set; }

    public decimal Quantity { get; set; } // positive for IN, negative for OUT in logic

    public decimal UnitCostAtTime { get; set; }

    public decimal TotalCost => Math.Abs(Quantity) * UnitCostAtTime;

    public string? Reference { get; set; } // e.g. "Job J-2026-00XX", "PO-123", "Adjustment"

    public Guid? JobId { get; set; }

    public string? Notes { get; set; }
}
