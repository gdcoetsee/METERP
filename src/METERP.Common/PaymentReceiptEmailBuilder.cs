using System.Globalization;

namespace METERP.Common;

/// <summary>
/// Customer receipt for a payment that is already committed.
/// Amounts are the stored VAT-inclusive rands; this does not recompute VAT.
/// </summary>
public static class PaymentReceiptEmailBuilder
{
    public static string BuildHtml(
        string invoiceNumber,
        decimal amountReceived,
        decimal invoiceTotal,
        decimal balanceDue)
    {
        return $"""
            <p>We have received your payment.</p>
            <ul>
              <li><strong>Invoice:</strong> {Escape(invoiceNumber)}</li>
              <li><strong>Amount received:</strong> {Money(amountReceived)}</li>
              <li><strong>Invoice total (VAT inclusive):</strong> {Money(invoiceTotal)}</li>
              <li><strong>Balance due:</strong> {Money(balanceDue)}</li>
            </ul>
            <p>Thank you.</p>
            """;
    }

    public static string Money(decimal amount) =>
        "R " + amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Escape(string? value) =>
        (value ?? "")
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
