namespace METERP.Application.Services;

/// <summary>
/// Lightweight summary for listing users (avoids leaking Infrastructure types into Application layer).
/// </summary>
public record UserSummary(Guid Id, string Email, string? UserName);

/// <summary>
/// Tenant-scoped user management service on top of ASP.NET Identity.
/// Used for creating and administering users within the current tenant.
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<UserSummary>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Creates a new user, sets tenant, hashes password, assigns to role, and ensures permission claims flow.
    /// </summary>
    Task<(bool Succeeded, string[] Errors)> CreateUserAsync(string email, string password, string role, CancellationToken ct = default);

    /// <summary>
    /// Changes the primary role for the user (removes other roles, adds the new one).
    /// </summary>
    Task<(bool Succeeded, string[] Errors)> ChangeUserRoleAsync(Guid userId, string newRole, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetAvailableRolesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Replaces direct Permission claims on the user (role template + custom overrides).
    /// </summary>
    Task<(bool Succeeded, string[] Errors)> SetUserPermissionsAsync(
        Guid userId,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default);
}
