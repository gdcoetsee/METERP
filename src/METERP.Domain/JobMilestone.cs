namespace METERP.Domain;

/// <summary>
/// Job milestone for timeline / Gantt-style progress tracking.
/// </summary>
public class JobMilestone : BaseEntity
{
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? DueDate { get; set; }

    public int PercentComplete { get; set; }

    public JobMilestoneStatus Status { get; set; } = JobMilestoneStatus.Pending;

    public int SortOrder { get; set; }
}