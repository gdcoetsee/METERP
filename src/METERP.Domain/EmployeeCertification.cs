namespace METERP.Domain;

/// <summary>
/// Employee licence / certification with expiry (tenant-defined types).
/// </summary>
public class EmployeeCertification : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public string CertificationType { get; set; } = string.Empty;

    public string? CertificateNumber { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public bool NoExpiry { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public int? LastExpiryAlertDaysRemaining { get; set; }
}