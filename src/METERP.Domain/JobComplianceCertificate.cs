namespace METERP.Domain;

/// <summary>
/// Job-level compliance certificate (CoC, test report, etc.) — tenant-defined types.
/// </summary>
public class JobComplianceCertificate : BaseEntity
{
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public string CertificateType { get; set; } = string.Empty;

    public string? CertificateNumber { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public DateTime? IssuedDate { get; set; }

    public bool NoExpiry { get; set; }

    public DateTime? ExpiryDate { get; set; }
}