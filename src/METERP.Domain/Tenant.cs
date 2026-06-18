namespace METERP.Domain;

/// <summary>
/// Represents a tenant in the multi-tenant system.
/// Note: Tenant management is typically handled with special care
/// (often not filtered by TenantId itself or treated as system-level).
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Unique subdomain or identifier for the tenant (e.g. "acme" -> acme.meterp.com).
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // === Commercial usage tracking (sellable / billing foundation) ===
    // Simple counters + activity for demo purposes. In production: time-series table, billing integration, quotas.
    public int TotalJobsCreated { get; set; }
    public int TotalAiCalls { get; set; }
    public int TotalQuotesCreated { get; set; }
    public int TotalInvoicesIssued { get; set; }
    public decimal TotalRevenueBilled { get; set; } // Rough for demo billing feel (from invoices)

    public DateTime? LastActivityUtc { get; set; }

    // === Subscription tier + enforceable monthly quotas (sellable SaaS) ===
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Starter;

    /// <summary>Start of the current monthly usage period (UTC, first day of month).</summary>
    public DateTime? UsagePeriodStartUtc { get; set; }

    public int PeriodQuotesCreated { get; set; }
    public int PeriodJobsCreated { get; set; }
    public int PeriodInvoicesIssued { get; set; }
    public int PeriodAiCalls { get; set; }

    /// <summary>Override tier defaults; null = use tier default (Enterprise = unlimited).</summary>
    public int? MaxQuotesPerMonth { get; set; }
    public int? MaxJobsPerMonth { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? MaxAiCallsPerMonth { get; set; }

    // === SaaS billing (Stripe / payment provider webhooks) ===
    /// <summary>Stripe customer id (cus_*) linked to this tenant for subscription webhooks.</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>Provider subscription status: active, trialing, past_due, canceled, unpaid.</summary>
    public string? SubscriptionStatus { get; set; }

    // === Integrations (per-tenant hooks for sellable SaaS) ===
    /// <summary>Optional HTTPS endpoint for invoice.created webhook payloads (Zapier, custom ERP, etc.).</summary>
    public string? InvoiceWebhookUrl { get; set; }

    /// <summary>Optional email for operational alerts (invoice created, low stock). Falls back to Email:DefaultNotificationTo.</summary>
    public string? NotificationEmail { get; set; }

    // === Per-tenant AI BYOK (optional override of deployment Ai:* settings) ===
    public string? AiProvider { get; set; }
    public string? AiApiKeyEncrypted { get; set; }
    public string? AiBaseUrl { get; set; }
    public string? AiModel { get; set; }
    public bool AiUseTenantKey { get; set; }

    // === Simple feature flag stub for sellable / tiered features (per README) ===
    // Comma-separated for demo (e.g. "ai,usage-tracking,advanced-reports"). In prod: proper flags service.
    public string EnabledFeatures { get; set; } = "ai,usage-tracking";

    public bool HasFeature(string feature)
    {
        if (string.IsNullOrWhiteSpace(EnabledFeatures) || string.IsNullOrWhiteSpace(feature))
            return false;
        var flags = EnabledFeatures.Split(',').Select(f => f.Trim().ToLowerInvariant());
        return flags.Contains(feature.Trim().ToLowerInvariant());
    }

    public int GetPeriodUsage(QuotaType type) =>
        type switch
        {
            QuotaType.Quote => PeriodQuotesCreated,
            QuotaType.Job => PeriodJobsCreated,
            QuotaType.Invoice => PeriodInvoicesIssued,
            QuotaType.AiCall => PeriodAiCalls,
            _ => 0
        };
}
