namespace METERP.Application.Models;

public sealed record CompanyDocumentExpiryRow(
    Guid Id,
    string Title,
    string DocumentType,
    DateTime ExpiryDate,
    int DaysRemaining,
    string Href);
