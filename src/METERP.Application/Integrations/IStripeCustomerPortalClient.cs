namespace METERP.Application.Integrations;

/// <summary>
/// Creates Stripe Billing Portal sessions (customer self-service subscription management).
/// </summary>
public interface IStripeCustomerPortalClient
{
    Task<string?> CreateSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default);
}