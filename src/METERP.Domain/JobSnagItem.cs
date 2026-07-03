namespace METERP.Domain;

/// <summary>
/// Snag / punch-list item on a job — field quality tracking.
/// </summary>
public class JobSnagItem : BaseEntity
{
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public Guid? ReportedByUserId { get; set; }

    public bool IsResolved { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }
}