namespace METERP.Application.Models;

public sealed record ReadyToInvoiceJobRow(
    Guid JobId,
    string JobNumber,
    string Title,
    string CustomerName,
    decimal QuotedTotal,
    decimal BilledToDate,
    decimal UnbilledResidual,
    string Reason = "Unbilled");
