using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Resolves Stripe Customer Portal (or compatible) URLs for tenant self-service billing.
/// </summary>
public interface IBillingPortalService
{
    bool IsConfigured { get; }

    string? GetCustomerPortalUrl(Tenant tenant);
}