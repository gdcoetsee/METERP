using System.ComponentModel.DataAnnotations;

namespace METERP.Domain;

/// <summary>
/// Base entity for all domain entities. Provides multi-tenancy (TenantId),
/// audit fields, and soft delete support.
/// </summary>
public abstract class BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Tenant identifier for multi-tenancy isolation.
    /// </summary>
    public Guid TenantId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? LastModifiedDate { get; set; }
    public string? LastModifiedBy { get; set; }

    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Optional concurrency token.
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
