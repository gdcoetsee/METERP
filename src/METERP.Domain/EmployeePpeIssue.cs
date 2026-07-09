namespace METERP.Domain;

/// <summary>
/// PPE stock issued to an employee (register). Job link is optional (site context only).
/// </summary>
public class EmployeePpeIssue : BaseEntity
{
    /// <summary>Primary holder — PPE is employee-centric, not job-primary.</summary>
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public Guid RequestedByUserId { get; set; }

    /// <summary>Optional site/job context when PPE is issued for a specific job.</summary>
    public Guid? JobId { get; set; }
    public Job? Job { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public Guid? StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    public decimal Quantity { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }
}