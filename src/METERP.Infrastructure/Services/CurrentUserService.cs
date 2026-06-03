using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using METERP.Application.Interfaces;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Default implementation that reads from the current HttpContext (Blazor Server / Web).
/// For background jobs or tests, we can have alternative implementations that set values manually.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantProvider _tenantProvider;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, ITenantProvider tenantProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantProvider = tenantProvider;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var idClaim = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }

    public Guid TenantId => _tenantProvider.GetCurrentTenantId();

    public string? UserName => _httpContextAccessor.HttpContext?.User?.Identity?.Name;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Permissions
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return Array.Empty<string>();

            return user.Claims
                .Where(c => c.Type == "Permission")
                .Select(c => c.Value)
                .ToList();
        }
    }
}
