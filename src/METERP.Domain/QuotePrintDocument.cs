using System.Globalization;
using System.Text;

namespace METERP.Domain;

/// <summary>
/// Text of a quote sent to a customer. The tenant VAT number and the customer VAT number
/// are included only when they are set. A blank number is left off — nothing is invented.
/// </summary>
public static class QuotePrintDocument
{
    public static string Build(
        Quote quote,
        string? tenantVatNumber,
        string? tenantName = null,
        string? customerVatNumber = null)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var office = string.IsNullOrWhiteSpace(tenantName) ? "Quote" : tenantName.Trim();
        var customerName = quote.Customer?.Name?.Trim() ?? "";
        var ourVat = OurVatLine(tenantVatNumber);
        var theirVat = CustomerVatLine(ResolveCustomerVat(customerVatNumber, quote.Customer?.VatNumber));

        var sb = new StringBuilder();
        sb.Append(office).Append(" — Quote").AppendLine();
        sb.Append("Quote: ").Append(quote.QuoteNumber?.Trim() ?? "").AppendLine();
        sb.Append("Date: ").Append(quote.QuoteDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Valid until: ").Append(quote.ValidUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine();
        if (ourVat != null)
            sb.AppendLine(ourVat);
        sb.Append("Customer: ").Append(customerName).AppendLine();
        if (theirVat != null)
            sb.AppendLine(theirVat);

        var lines = quote.Lines?.Where(l => !l.IsDeleted).ToList();
        if (lines is { Count: > 0 })
        {
            sb.AppendLine("Lines:");
            foreach (var line in lines)
            {
                var description = string.IsNullOrWhiteSpace(line.Description) ? "Line" : line.Description.Trim();
                sb.Append(description)
                    .Append("  ")
                    .Append(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append(" x R ")
                    .Append(Money(line.UnitPrice))
                    .Append("  R ")
                    .Append(Money(line.LineTotal))
                    .AppendLine();
            }
        }

        sb.Append("Subtotal: R ").Append(Money(quote.Subtotal)).AppendLine();
        sb.Append("VAT: R ").Append(Money(quote.Tax)).AppendLine();
        sb.Append("Total: R ").Append(Money(quote.Total)).AppendLine();
        return sb.ToString();
    }

    /// <summary>Null when the tenant has no VAT number. Never a placeholder.</summary>
    public static string? OurVatLine(string? tenantVatNumber)
    {
        var vat = Clean(tenantVatNumber);
        return vat == null ? null : $"Our VAT number: {vat}";
    }

    /// <summary>Null when the customer has no VAT number. Never a placeholder.</summary>
    public static string? CustomerVatLine(string? customerVatNumber)
    {
        var vat = Clean(customerVatNumber);
        return vat == null ? null : $"Customer VAT number: {vat}";
    }

    /// <summary>First non-blank VAT number. Does not copy the tenant number into the customer line.</summary>
    public static string? ResolveCustomerVat(string? preferred, string? fallback) =>
        Clean(preferred) ?? Clean(fallback);

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
