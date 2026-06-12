using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Shared tenant feature + quota checks for AI apply paths.
/// </summary>
internal static class AiCopilotAccessGuard
{
    public static async Task EnsureAiApplyAllowedAsync(
        ITenantProvider? tenantProvider,
        ITenantService? tenantService,
        CancellationToken ct = default)
    {
        var tenantId = tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (tenantId == Guid.Empty || tenantService == null)
            return;

        var tenant = await tenantService.GetByIdAsync(tenantId, ct);
        if (tenant != null && !tenant.HasFeature("ai"))
            throw new AiFeatureDisabledException();
    }
}