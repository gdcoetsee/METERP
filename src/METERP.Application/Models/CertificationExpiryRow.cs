namespace METERP.Application.Models;

public sealed record CertificationExpiryRow(
    Guid Id,
    string EmployeeName,
    string CertificationType,
    DateTime ExpiryDate,
    int DaysRemaining,
    string Href);
