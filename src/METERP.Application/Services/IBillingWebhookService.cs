namespace METERP.Application.Services;

/// <summary>
/// Processes payment-provider billing webhooks (Stripe-compatible) to sync tenant tier and subscription status.
/// </summary>
public interface IBillingWebhookService
{
    Task<BillingWebhookResult> ProcessStripeEventAsync(
        string rawBody,
        string? stripeSignatureHeader,
        bool allowUnsignedForDev,
        CancellationToken ct = default);
}

public enum BillingWebhookOutcome
{
    Ignored,
    TierUpdated,
    SubscriptionCanceled,
    CustomerLinked,
    Duplicate,
    InvalidSignature,
    InvalidPayload
}

public sealed record BillingWebhookResult(BillingWebhookOutcome Outcome, string? Message = null)
{
    public static BillingWebhookResult InvalidSignature() =>
        new(BillingWebhookOutcome.InvalidSignature);

    public static BillingWebhookResult InvalidPayload(string? detail = null) =>
        new(BillingWebhookOutcome.InvalidPayload, detail);
}