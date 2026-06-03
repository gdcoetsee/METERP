using Microsoft.AspNetCore.Identity;

namespace METERP.Infrastructure.Identity;

/// <summary>
/// Application role for ASP.NET Identity, extended for multi-tenancy.
/// Roles can be tenant-specific (e.g. "Admin" per tenant).
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public Guid TenantId { get; set; }

    // We will store permissions as claims on roles for flexibility.
}
