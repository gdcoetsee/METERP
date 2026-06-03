using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using METERP.Application.Interfaces;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Default implementation of ITenantProvider.
/// Reads TenantId from the logged-in user's claims when available (set during login/seed).
/// Falls back to manual SetTenantId (useful for demo tenant switcher or background work).
/// </summary>
public class CurrentTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private Guid _currentTenantId = Guid.Empty;

    public CurrentTenantProvider(IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetCurrentTenantId()
    {
        // Prefer from authenticated user claims (set by Identity on login)
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = user.FindFirst("TenantId")?.Value;
            if (Guid.TryParse(tenantClaim, out var tenantFromClaim))
            {
                _currentTenantId = tenantFromClaim;
                return _currentTenantId;
            }
        }

        return _currentTenantId;
    }

    public void SetTenantId(Guid tenantId)
    {
        _currentTenantId = tenantId;
    }
}
