using Microsoft.AspNetCore.Identity;

namespace METERP.Infrastructure.Identity;

/// <summary>
/// Application user for ASP.NET Identity, extended for multi-tenancy.
/// Every user belongs to exactly one tenant.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    // Future extensibility for contracting ERP:
    // public string FullName { get; set; } = string.Empty;
    // public string? PhoneNumber { get; set; }
}
