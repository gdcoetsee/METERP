namespace METERP.Domain;

/// <summary>
/// Tenant company compliance document (COID, tax clearance, insurance, etc.) with optional expiry.
/// </summary>
public class CompanyDocument : BaseEntity
{
    public string DocumentType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public bool NoExpiry { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Tracks last expiry alert threshold sent (30, 14, or 7 days).</summary>
    public int? LastExpiryAlertDaysRemaining { get; set; }
}