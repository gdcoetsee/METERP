namespace METERP.Domain;

/// <summary>
/// Commercial document classification for contractor billing (deposit, proforma, partial, final, credit note).
/// Credit notes store a positive <see cref="Invoice.Total"/>; <see cref="InvoiceCreditConvention"/> is the display and AR rule.
/// </summary>
public enum InvoiceDocumentType
{
    Standard = 0,
    Proforma = 1,
    Deposit = 2,
    Partial = 3,
    Final = 4,
    CreditNote = 5
}