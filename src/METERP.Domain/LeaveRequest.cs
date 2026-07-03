namespace METERP.Domain;

/// <summary>
/// Employee leave request with manager → executive → tenant HR approval chain.
/// </summary>
public class LeaveRequest : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public decimal DaysRequested { get; set; }

    public bool IsPaid { get; set; } = true;

    public string? Reason { get; set; }

    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.PendingManager;

    public Guid? ManagerApprovedByUserId { get; set; }
    public DateTime? ManagerApprovedAt { get; set; }

    public Guid? ExecutiveApprovedByUserId { get; set; }
    public DateTime? ExecutiveApprovedAt { get; set; }

    public Guid? HrApprovedByUserId { get; set; }
    public DateTime? HrApprovedAt { get; set; }

    public string? RejectionReason { get; set; }
}