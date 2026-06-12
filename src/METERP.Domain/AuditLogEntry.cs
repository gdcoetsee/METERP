namespace METERP.Domain;

/// <summary>
/// Immutable-style audit trail entry for compliance and sellable enterprise story.
/// </summary>
public class AuditLogEntry : BaseEntity
{
    public string UserEmail { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityReference { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}