using Microsoft.AspNetCore.Identity;

namespace METERP.Infrastructure.Identity;

/// <summary>
/// Application user for ASP.NET Identity, extended for multi-tenancy.
/// Every user belongs to exactly one tenant.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    /// <summary>When set, this login is a customer-portal user scoped to that customer only.</summary>
    public Guid? CustomerId { get; set; }
}
