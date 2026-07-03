namespace METERP.Domain;

/// <summary>
/// Technician field report — hours, travel, materials notes; manager approval posts to job costing.
/// </summary>
public class FieldReport : BaseEntity
{
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public Guid SubmittedByUserId { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    public decimal HoursWorked { get; set; }

    public decimal TravelCost { get; set; }

    public string? MaterialsUsed { get; set; }

    public string? Comments { get; set; }

    public FieldReportStatus Status { get; set; } = FieldReportStatus.PendingApproval;

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public string? RejectionReason { get; set; }
}