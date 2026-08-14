namespace METERP.Application.Services;

public sealed record InvoiceChaseResult(
    Guid InvoiceId,
    string InvoiceNumber,
    bool EmailSent,
    string? CustomerEmail,
    int DaysOverdue,
    decimal BalanceDue);
