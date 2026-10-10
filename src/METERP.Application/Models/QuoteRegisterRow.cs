using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// One line of the quote book: date, number, customer, stored ex-VAT and VAT, total, status, and whether a job exists.
/// VAT is the quote's stored <see cref="Quote.Tax"/>, not a fresh Subtotal × TaxRate.
/// </summary>
public sealed record QuoteRegisterRow(
    Guid Id,
    string QuoteNumber,
    DateTime QuoteDate,
    string CustomerName,
    decimal ExVat,
    decimal Vat,
    decimal Total,
    QuoteStatus Status,
    bool HasJob);
