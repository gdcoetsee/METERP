namespace METERP.Application.Models;

public sealed record PpeOutstandingRow(
    Guid Id,
    string EmployeeName,
    string ItemName,
    decimal Outstanding,
    DateTime IssuedAt,
    string Href);
