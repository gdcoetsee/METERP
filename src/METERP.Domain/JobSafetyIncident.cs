namespace METERP.Domain;

/// <summary>
/// Safety incident recorded against a job site.
/// </summary>
public class JobSafetyIncident : BaseEntity
{
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public string Description { get; set; } = string.Empty;

    public SafetyIncidentSeverity Severity { get; set; } = SafetyIncidentSeverity.Medium;

    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public Guid? ReportedByUserId { get; set; }

    public bool IsClosed { get; set; }

    public DateTime? ClosedAt { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public string? CorrectiveAction { get; set; }
}