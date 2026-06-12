using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Read model for tenant-facing billing pages (refreshed after Stripe webhooks update tier/status).
/// </summary>
public interface ITenantBillingViewService
{
    Task<TenantBillingView?> GetAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record TenantBillingView(
    Tenant Tenant,
    bool HasAiFeature,
    bool IsPastDue);