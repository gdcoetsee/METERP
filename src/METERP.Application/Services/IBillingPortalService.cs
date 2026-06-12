using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Resolves Stripe Customer Portal (or compatible) URLs for tenant self-service billing.
/// </summary>
public interface IBillingPortalService
{
    bool IsConfigured { get; }

    /// <summary>Static/demo portal URL with customer query param (no Stripe API call).</summary>
    string? GetCustomerPortalUrl(Tenant tenant);

    /// <summary>Stripe API session when configured; otherwise <see cref="GetCustomerPortalUrl"/>.</summary>
    Task<string?> ResolveCustomerPortalUrlAsync(Tenant tenant, CancellationToken ct = default);
}