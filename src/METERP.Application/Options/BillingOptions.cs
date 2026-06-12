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

    public bool IsSignatureRequired => !string.IsNullOrWhiteSpace(WebhookSecret);
}