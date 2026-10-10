namespace METERP.Application.Services;

/// <summary>
/// A customer whose open credit notes exceed open invoices.
/// <see cref="CreditAmount"/> is the positive VAT-inclusive excess.
/// </summary>
public record CustomerInCreditRow(
    Guid CustomerId,
    string CustomerName,
    decimal CreditAmount);
