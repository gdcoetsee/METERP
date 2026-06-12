namespace METERP.Domain;

/// <summary>
/// Idempotency ledger for Stripe webhook event IDs (prevents duplicate tier/status updates on retries).
/// System-level — not tenant-scoped.
/// </summary>
public class ProcessedStripeWebhookEvent
{
    public string EventId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}