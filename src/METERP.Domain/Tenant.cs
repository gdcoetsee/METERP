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

    // Future: connection string override, settings, subscription tier, quotas, etc.
}
