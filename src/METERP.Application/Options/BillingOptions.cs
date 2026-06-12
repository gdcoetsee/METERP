namespace METERP.Application.Options;

/// <summary>
/// Stripe (or compatible) billing webhook settings for SaaS subscription sync.
/// When WebhookSecret is empty, unsigned payloads are accepted only in Development/Testing.
/// </summary>
public class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Stripe webhook signing secret (whsec_*).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Stripe secret API key (sk_test_* / sk_live_*). Enables real portal session creation.</summary>
    public string StripeSecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for Stripe Customer Portal login (e.g. https://billing.stripe.com/p/login/...).
    /// Demo fallback when <see cref="StripeSecretKey"/> is not set.
    /// </summary>
    public string CustomerPortalBaseUrl { get; set; } = string.Empty;

    /// <summary>Absolute URL Stripe redirects to after portal (required for API sessions).</summary>
    public string CustomerPortalReturnUrl { get; set; } = string.Empty;

    public bool IsSignatureRequired => !string.IsNullOrWhiteSpace(WebhookSecret);

    public bool IsApiConfigured => !string.IsNullOrWhiteSpace(StripeSecretKey);

    public bool CanCreateApiSessions =>
        IsApiConfigured && !string.IsNullOrWhiteSpace(CustomerPortalReturnUrl);

    public bool IsPortalConfigured =>
        CanCreateApiSessions || !string.IsNullOrWhiteSpace(CustomerPortalBaseUrl);
}