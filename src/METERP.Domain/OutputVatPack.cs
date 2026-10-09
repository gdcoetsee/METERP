using System.Globalization;
using System.Text;

namespace METERP.Domain;

/// <summary>
/// One issued document on the output VAT worksheet.
/// Net, VAT, and gross are the stored amounts. A credit note is negative.
/// </summary>
public sealed record OutputVatLine(
    DateTime Date,
    string Number,
    string Customer,
    decimal Net,
    decimal Vat,
    decimal Gross);

/// <summary>
/// Output VAT for a calendar range. Totals are the sum of <see cref="Lines"/>.
/// <see cref="ToCsv"/> prints those same totals, which is what the Finance screen shows.
/// </summary>
public sealed record OutputVatPack(
    DateTime From,
    DateTime To,
    IReadOnlyList<OutputVatLine> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal)
{
    public string FileName => $"output-vat-{From:yyyyMMdd}-{To:yyyyMMdd}.csv";

    public static string FormatAmount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public string ToCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Number,Customer,Net,VAT,Gross");
        foreach (var line in Lines)
        {
            sb.Append(line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(line.Number)).Append(',')
                .Append(Csv(line.Customer)).Append(',')
                .Append(FormatAmount(line.Net)).Append(',')
                .Append(FormatAmount(line.Vat)).Append(',')
                .Append(FormatAmount(line.Gross))
                .AppendLine();
        }

        sb.Append(",,Total,")
            .Append(FormatAmount(NetTotal)).Append(',')
            .Append(FormatAmount(VatTotal)).Append(',')
            .Append(FormatAmount(GrossTotal))
            .AppendLine();
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}

/// <summary>
/// A billing document handed to the VAT pack. Amounts are already stored.
/// Do not split the gross again — Access totals were split once.
/// </summary>
public readonly record struct OutputVatDocument(
    DateTime InvoiceDate,
    InvoiceDocumentType DocumentType,
    InvoiceStatus Status,
    string? InvoiceNumber,
    string? CustomerName,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    bool IsDeleted);

/// <summary>
/// Builds the monthly output VAT pack from stored subtotal, tax, and total.
/// Issued documents are Sent, PartiallyPaid, Paid, and Overdue.
/// Draft, proforma, and cancelled documents are left out.
/// A credit note subtracts its stored tax and total (and the stored net).
/// </summary>
public static class OutputVatPackBuilder
{
    public static OutputVatPack Build(DateTime from, DateTime to, IEnumerable<OutputVatDocument>? documents)
    {
        var start = from.Date;
        var end = to.Date;
        if (end < start)
            throw new InvalidOperationException("VAT pack end date is before the start date.");

        var lines = new List<OutputVatLine>();
        foreach (var document in documents ?? Array.Empty<OutputVatDocument>())
        {
            if (!TryLine(document, start, end, out var line))
                continue;
            lines.Add(line);
        }

        lines.Sort(static (a, b) =>
        {
            var byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0)
                return byDate;
            return string.Compare(a.Number, b.Number, StringComparison.OrdinalIgnoreCase);
        });

        var net = 0m;
        var vat = 0m;
        var gross = 0m;
        foreach (var line in lines)
        {
            net += line.Net;
            vat += line.Vat;
            gross += line.Gross;
        }

        return new OutputVatPack(
            start,
            end,
            lines,
            Math.Round(net, 2, MidpointRounding.AwayFromZero),
            Math.Round(vat, 2, MidpointRounding.AwayFromZero),
            Math.Round(gross, 2, MidpointRounding.AwayFromZero));
    }

    private static bool TryLine(OutputVatDocument document, DateTime start, DateTime end, out OutputVatLine line)
    {
        line = null!;
        if (document.IsDeleted)
            return false;

        var date = document.InvoiceDate.Date;
        if (date < start || date > end)
            return false;

        if (document.DocumentType == InvoiceDocumentType.Proforma)
            return false;

        if (document.Status is not (
            InvoiceStatus.Sent
            or InvoiceStatus.PartiallyPaid
            or InvoiceStatus.Paid
            or InvoiceStatus.Overdue))
            return false;

        var net = Math.Round(document.Subtotal, 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round(document.Tax, 2, MidpointRounding.AwayFromZero);
        var gross = Math.Round(document.Total, 2, MidpointRounding.AwayFromZero);
        if (document.DocumentType == InvoiceDocumentType.CreditNote)
        {
            net = -Math.Abs(net);
            vat = -Math.Abs(vat);
            gross = -Math.Abs(gross);
        }

        var number = string.IsNullOrWhiteSpace(document.InvoiceNumber)
            ? "Document"
            : document.InvoiceNumber.Trim();
        var customer = string.IsNullOrWhiteSpace(document.CustomerName)
            ? "Customer"
            : document.CustomerName.Trim();

        line = new OutputVatLine(date, number, customer, net, vat, gross);
        return true;
    }
}
