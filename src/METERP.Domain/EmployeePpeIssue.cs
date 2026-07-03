namespace METERP.Domain;

/// <summary>
/// PPE / safety equipment issued to a field worker — executive visibility & compliance history.
/// </summary>
public class EmployeePpeIssue : BaseEntity
{
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public Guid RequestedByUserId { get; set; }

    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public Guid? StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    public decimal Quantity { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }
}