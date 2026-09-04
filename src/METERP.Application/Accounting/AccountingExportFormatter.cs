using System.Globalization;
using System.Text;
using METERP.Domain;

namespace METERP.Application.Accounting;

/// <summary>
/// Pure Sage / Xero sales-invoice CSV builders. No I/O — unit tested.
/// Sage: Business Cloud / 50-compatible sales daybook.
/// Xero: official sales invoice CSV import columns.
/// </summary>
public static class AccountingExportFormatter
{
    public static string BuildCsv(AccountingProvider provider, IEnumerable<Invoice> invoices, string salesAccountCode)
    {
        var code = string.IsNullOrWhiteSpace(salesAccountCode) ? "200" : salesAccountCode.Trim();
        return provider switch
        {
            AccountingProvider.Xero => BuildXeroCsv(invoices, code),
            AccountingProvider.Sage => BuildSageCsv(invoices, code),
            _ => throw new InvalidOperationException("Choose Sage or Xero before exporting.")
        };
    }

    public static string FileName(AccountingProvider provider, DateTime utcNow) =>
        provider == AccountingProvider.Xero
            ? $"xero-invoices-{utcNow:yyyyMMdd}.csv"
            : $"sage-invoices-{utcNow:yyyyMMdd}.csv";

    public static string BuildSageCsv(IEnumerable<Invoice> invoices, string accountCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Type,Account Number,Account Name,Date,Reference,Details,Net Amount,Tax Amount,Gross Amount,Tax Rate");
        foreach (var invoice in VisibleInvoices(invoices))
        {
            var customer = Csv(invoice.Customer?.Name ?? "Customer");
            var date = invoice.InvoiceDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var reference = Csv(invoice.InvoiceNumber);
            var type = invoice.DocumentType == InvoiceDocumentType.CreditNote ? "Sales Credit" : "Sales Invoice";
            foreach (var line in VisibleLines(invoice))
            {
                var net = line.LineTotal;
                var tax = Math.Round(net * invoice.TaxRate, 2);
                sb.Append(type).Append(',')
                    .Append(Csv(accountCode)).Append(',')
                    .Append(customer).Append(',')
                    .Append(date).Append(',')
                    .Append(reference).Append(',')
                    .Append(Csv(line.Description)).Append(',')
                    .Append(Inv(net)).Append(',')
                    .Append(Inv(tax)).Append(',')
                    .Append(Inv(net + tax)).Append(',')
                    .Append(Inv(invoice.TaxRate * 100))
                    .AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string BuildXeroCsv(IEnumerable<Invoice> invoices, string accountCode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*ContactName,*InvoiceNumber,*InvoiceDate,*DueDate,Reference,*Description,*Quantity,*UnitAmount,*AccountCode,*TaxType,Currency");
        foreach (var invoice in VisibleInvoices(invoices))
        {
            var taxType = invoice.TaxRate > 0 ? "OUTPUT2" : "NONE";
            foreach (var line in VisibleLines(invoice))
            {
                sb.Append(Csv(invoice.Customer?.Name ?? "Customer")).Append(',')
                    .Append(Csv(invoice.InvoiceNumber)).Append(',')
                    .Append(invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(invoice.Job?.JobNumber)).Append(',')
                    .Append(Csv(line.Description)).Append(',')
                    .Append(Inv(line.Quantity)).Append(',')
                    .Append(Inv(line.UnitPrice)).Append(',')
                    .Append(Csv(accountCode)).Append(',')
                    .Append(taxType).Append(',')
                    .Append("ZAR")
                    .AppendLine();
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<Invoice> VisibleInvoices(IEnumerable<Invoice> invoices) =>
        invoices.Where(i => !i.IsDeleted && i.Status is not InvoiceStatus.Draft and not InvoiceStatus.Cancelled);

    private static IEnumerable<InvoiceLine> VisibleLines(Invoice invoice)
    {
        var lines = (invoice.Lines ?? []).Where(l => !l.IsDeleted).ToList();
        if (lines.Count > 0)
            return lines;

        return
        [
            new InvoiceLine
            {
                Description = string.IsNullOrWhiteSpace(invoice.Notes) ? invoice.InvoiceNumber : invoice.Notes,
                Quantity = 1,
                UnitPrice = invoice.Subtotal
            }
        ];
    }

    private static string Inv(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        var v = value ?? "";
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
