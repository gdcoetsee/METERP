namespace METERP.Domain;

/// <summary>
/// Material requisition for a job — field or stores initiated, manager → executive approval, then issue or procure.
/// </summary>
public class StockRequisition : BaseEntity
{
    public string RequisitionNumber { get; set; } = string.Empty;

    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public Guid RequestedByUserId { get; set; }

    public RequisitionStatus Status { get; set; } = RequisitionStatus.PendingManager;

    public bool IsPpe { get; set; }

    public string? Notes { get; set; }

    public Guid? ManagerApprovedByUserId { get; set; }
    public DateTime? ManagerApprovedAt { get; set; }

    public Guid? ExecutiveApprovedByUserId { get; set; }
    public DateTime? ExecutiveApprovedAt { get; set; }

    public Guid? IssuedByUserId { get; set; }
    public DateTime? IssuedAt { get; set; }

    public string? RejectionReason { get; set; }

    public Guid? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public ICollection<StockRequisitionLine> Lines { get; set; } = new List<StockRequisitionLine>();
}