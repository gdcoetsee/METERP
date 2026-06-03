namespace METERP.Application.Interfaces;

/// <summary>
/// Provides information about the currently authenticated user in a clean way
/// (decoupled from HttpContext/ClaimsPrincipal for use in Application/Infrastructure).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid TenantId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Permissions { get; }
}
