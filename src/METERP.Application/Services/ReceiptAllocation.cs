namespace METERP.Application.Services;

/// <summary>One slice of a single bank receipt applied to one invoice.</summary>
public record ReceiptAllocationLine(Guid InvoiceId, decimal Amount);

/// <summary>
/// Open sales invoice a clerk can include on one receipt.
/// Draft, proforma, cancelled, credit notes, and zero balances are omitted.
/// </summary>
public record AllocatableInvoiceRow(
    Guid InvoiceId,
    string InvoiceNumber,
    DateTime InvoiceDate,
    decimal BalanceDue);
