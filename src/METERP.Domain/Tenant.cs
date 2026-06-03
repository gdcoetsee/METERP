namespace METERP.Domain;

/// <summary>
/// Represents a tenant in the multi-tenant system.
/// Note: Tenant management is typically handled with special care
/// (often not filtered by TenantId itself or treated as system-level).
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique subdomain or identifier for the tenant (e.g. "acme" -> acme.meterp.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Future: connection string override, settings, etc.
}
