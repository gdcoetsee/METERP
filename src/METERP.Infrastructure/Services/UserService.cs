using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Implementation of tenant-scoped user management using ASP.NET Core Identity.
/// </summary>
public class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ITenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public UserService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ITenantProvider tenantProvider,
        AppDbContext dbContext,
        ITenantCacheService? cache = null)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tenantProvider = tenantProvider;
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<IReadOnlyList<UserSummary>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (_cache != null && string.IsNullOrWhiteSpace(search))
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Users,
                $"p{page}:s{pageSize}",
                () => LoadUsersAsync(search, page, pageSize, ct),
                ct: ct);
        }

        return await LoadUsersAsync(search, page, pageSize, ct);
    }

    private async Task<IReadOnlyList<UserSummary>> LoadUsersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.Users.AsQueryable(); // Respects the global tenant query filter

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(term)));
        }

        var list = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return list.Select(u => new UserSummary(u.Id, u.Email ?? "", u.UserName)).ToList();
    }

    public async Task<(bool Succeeded, string[] Errors)> CreateUserAsync(string email, string password, string role, CancellationToken ct = default)
    {
        var currentTenant = _tenantProvider.GetCurrentTenantId();

        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            TenantId = currentTenant
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return (false, createResult.Errors.Select(e => e.Description).ToArray());
        }

        // Assign role
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                return (false, new[] { $"Role '{role}' does not exist for this tenant." });
            }

            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                return (false, roleResult.Errors.Select(e => e.Description).ToArray());
            }
        }

        // Add TenantId claim so the CurrentTenantProvider can pick it up on next login
        await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("TenantId", user.TenantId.ToString()));

        // Mirror permission claims from the role (defense in depth)
        var roleEntity = await _roleManager.FindByNameAsync(role);
        if (roleEntity != null)
        {
            var roleClaims = await _roleManager.GetClaimsAsync(roleEntity);
            foreach (var claim in roleClaims.Where(c => c.Type == "Permission"))
            {
                await _userManager.AddClaimAsync(user, claim);
            }
        }

        InvalidateListCaches();
        return (true, Array.Empty<string>());
    }

    // Internal helper (not on interface) for cases where full entity is needed inside infrastructure
    private async Task<ApplicationUser?> GetUserEntityByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<(bool Succeeded, string[] Errors)> ChangeUserRoleAsync(Guid userId, string newRole, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return (false, new[] { "User not found." });

        var currentRoles = await _userManager.GetRolesAsync(user);

        // Remove all current roles
        if (currentRoles.Any())
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
                return (false, removeResult.Errors.Select(e => e.Description).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(newRole))
        {
            if (!await _roleManager.RoleExistsAsync(newRole))
                return (false, new[] { $"Role '{newRole}' does not exist." });

            var addResult = await _userManager.AddToRoleAsync(user, newRole);
            if (!addResult.Succeeded)
                return (false, addResult.Errors.Select(e => e.Description).ToArray());

            // Re-sync permission claims from the new role
            var roleEntity = await _roleManager.FindByNameAsync(newRole);
            if (roleEntity != null)
            {
                // Remove old permission claims
                var existingClaims = await _userManager.GetClaimsAsync(user);
                foreach (var pc in existingClaims.Where(c => c.Type == "Permission"))
                {
                    await _userManager.RemoveClaimAsync(user, pc);
                }

                // Add from new role
                var roleClaims = await _roleManager.GetClaimsAsync(roleEntity);
                foreach (var claim in roleClaims.Where(c => c.Type == "Permission"))
                {
                    await _userManager.AddClaimAsync(user, claim);
                }
            }
        }

        return (true, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Array.Empty<string>();

        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<IReadOnlyList<string>> GetAvailableRolesAsync(CancellationToken ct = default)
    {
        if (_cache != null)
        {
            return await _cache.GetOrCreateAsync(
                TenantCacheCategories.Roles,
                "all",
                () => LoadAvailableRolesAsync(ct),
                ct: ct);
        }

        return await LoadAvailableRolesAsync(ct);
    }

    private async Task<IReadOnlyList<string>> LoadAvailableRolesAsync(CancellationToken ct)
    {
        var roles = await _dbContext.Roles
            .OrderBy(r => r.Name)
            .Select(r => r.Name!)
            .ToListAsync(ct);

        return roles;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Array.Empty<string>();

        var claims = await _userManager.GetClaimsAsync(user);
        return claims.Where(c => c.Type == "Permission").Select(c => c.Value).OrderBy(v => v).ToList();
    }

    public async Task<(bool Succeeded, string[] Errors)> SetUserPermissionsAsync(
        Guid userId,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
            return (false, new[] { "User not found." });

        var normalized = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingClaims = await _userManager.GetClaimsAsync(user);
        foreach (var claim in existingClaims.Where(c => c.Type == "Permission"))
        {
            await _userManager.RemoveClaimAsync(user, claim);
        }

        foreach (var perm in normalized)
        {
            await _userManager.AddClaimAsync(user, new System.Security.Claims.Claim("Permission", perm));
        }

        InvalidateListCaches();
        return (true, Array.Empty<string>());
    }

    private void InvalidateListCaches()
    {
        if (_cache != null)
            TenantCacheInvalidation.OnIdentityMutated(_cache);
    }
}
