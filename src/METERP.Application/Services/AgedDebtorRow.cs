namespace METERP.Application.Services;

public record AgedDebtorRow(
    Guid InvoiceId,
    string InvoiceNumber,
    string CustomerName,
    DateTime DueDate,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    int DaysOverdue,
    string AgingBucket);