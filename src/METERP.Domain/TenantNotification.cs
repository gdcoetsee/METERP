namespace METERP.Domain;

/// <summary>
/// Tenant-scoped in-app notification (compliance alerts, approvals, etc.).
/// </summary>
public class TenantNotification : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Category { get; set; } = "general";

    /// <summary>Comma-separated role names, or * for all authenticated users in tenant.</summary>
    public string TargetRoles { get; set; } = "*";

    public bool IsRead { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public string? RelatedEntityType { get; set; }
}