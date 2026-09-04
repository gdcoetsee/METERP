namespace METERP.Domain;

/// <summary>Accounting package used for invoice/credit export (Sage or Xero CSV + webhook).</summary>
public enum AccountingProvider
{
    None = 0,
    Sage = 1,
    Xero = 2
}
