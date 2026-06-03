using METERP.Application.Interfaces;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Default implementation of ITenantProvider.
/// In a real Blazor Server / Web app this would typically read the tenant
/// from the authenticated user's claims, the request host/subdomain,
/// or a selected tenant in the UI session.
/// </summary>
public class CurrentTenantProvider : ITenantProvider
{
    private Guid _currentTenantId = Guid.Empty;

    public Guid GetCurrentTenantId() => _currentTenantId;

    public void SetTenantId(Guid tenantId)
    {
        _currentTenantId = tenantId;
    }
}
