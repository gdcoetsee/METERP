namespace METERP.Application.Interfaces;

/// <summary>
/// Provides the current tenant context for multi-tenancy.
/// Implementations can resolve tenant from claims, headers, subdomain, etc.
/// </summary>
public interface ITenantProvider
{
    Guid GetCurrentTenantId();

    /// <summary>
    /// Sets the tenant for the current scope (useful for background jobs, tests, admin operations).
    /// </summary>
    void SetTenantId(Guid tenantId);
}
