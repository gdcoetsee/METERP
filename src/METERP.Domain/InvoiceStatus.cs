namespace METERP.Domain;

/// <summary>
/// Status for customer invoices.
/// </summary>
public enum InvoiceStatus
{
    Draft = 0,
    Sent = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5
}
