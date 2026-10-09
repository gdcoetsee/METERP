namespace METERP.Domain;

/// <summary>
/// One line on a customer account statement. Debit increases what the customer owes.
/// Credit decreases it. Amounts are VAT-inclusive rands already stored on the document.
/// </summary>
public sealed record CustomerStatementLine(
    DateTime Date,
    string Kind,
    string Reference,
    string Detail,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance);

/// <summary>
/// Account statement for one customer as at a calendar date.
/// <see cref="ClosingBalance"/> is positive when the customer owes MET.
/// A negative balance means the customer is in credit — display with
/// <see cref="InvoiceCreditConvention.FormatCustomerBalance"/>.
/// </summary>
public sealed record CustomerStatement(
    Guid CustomerId,
    string CustomerName,
    DateTime AsOf,
    IReadOnlyList<CustomerStatementLine> Lines,
    decimal ClosingBalance)
{
    public string ClosingBalanceDisplay => InvoiceCreditConvention.FormatCustomerBalance(ClosingBalance);
}

/// <summary>
/// A document handed to the statement builder. Payments may be empty when Access
/// stored the collected amount on <see cref="AmountPaid"/> and never wrote a receipt row.
/// </summary>
public readonly record struct CustomerStatementDocument(
    DateTime InvoiceDate,
    InvoiceDocumentType DocumentType,
    InvoiceStatus Status,
    string? InvoiceNumber,
    decimal Total,
    decimal AmountPaid,
    bool IsDeleted,
    IReadOnlyList<CustomerStatementReceipt> Payments);

public readonly record struct CustomerStatementReceipt(
    DateTime PaymentDate,
    decimal Amount,
    string? Reference,
    bool IsDeleted);

/// <summary>
/// Builds a customer statement from stored documents. Does not recompute VAT.
/// Draft, proforma, and cancelled documents are omitted. A credit note is a credit
/// for its positive total; <see cref="CustomerStatementDocument.AmountPaid"/> on that
/// credit note is the part already settled (Access Sold / Completed AIP) and is put
/// back so it does not leave a false credit. Cash recorded only as AmountPaid, with
/// no receipt row, is a Collected line on the invoice date.
/// </summary>
public static class CustomerStatementBuilder
{
    public static CustomerStatement Build(
        Guid customerId,
        string? customerName,
        DateTime asOf,
        IEnumerable<CustomerStatementDocument>? documents)
    {
        var cutOff = asOf.Date;
        var movements = new List<Movement>();

        foreach (var document in documents ?? Array.Empty<CustomerStatementDocument>())
        {
            if (document.IsDeleted)
                continue;
            if (document.InvoiceDate.Date > cutOff)
                continue;
            if (document.DocumentType == InvoiceDocumentType.Proforma)
                continue;
            if (document.Status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
                continue;

            var total = Math.Round(Math.Abs(document.Total), 2);
            var number = string.IsNullOrWhiteSpace(document.InvoiceNumber)
                ? "Document"
                : document.InvoiceNumber.Trim();

            if (InvoiceCreditConvention.IsCreditNote(document.DocumentType))
            {
                if (total > 0m)
                {
                    movements.Add(new Movement(
                        document.InvoiceDate.Date, 1, "Credit note", number, "VAT-inclusive credit", 0m, total));
                }

                var applied = Math.Round(Math.Abs(document.AmountPaid), 2);
                if (applied > 0m)
                {
                    movements.Add(new Movement(
                        document.InvoiceDate.Date, 2, "Credit applied", number,
                        "Settled against the account", applied, 0m));
                }

                continue;
            }

            if (total <= 0m && document.AmountPaid == 0m && !HasLiveReceipt(document))
                continue;

            movements.Add(new Movement(
                document.InvoiceDate.Date, 0, "Invoice", number, DocumentDetail(document.DocumentType), total, 0m));

            var paymentSum = 0m;
            foreach (var payment in document.Payments ?? Array.Empty<CustomerStatementReceipt>())
            {
                if (payment.IsDeleted)
                    continue;

                var amount = Math.Round(Math.Abs(payment.Amount), 2);
                if (amount <= 0m)
                    continue;

                paymentSum = Math.Round(paymentSum + amount, 2);
                if (payment.PaymentDate.Date > cutOff)
                    continue;

                var reference = string.IsNullOrWhiteSpace(payment.Reference)
                    ? number
                    : payment.Reference.Trim();
                movements.Add(new Movement(
                    payment.PaymentDate.Date, 4, "Receipt", reference, "Payment", 0m, amount));
            }

            var collected = Math.Round(Math.Abs(document.AmountPaid) - paymentSum, 2);
            if (collected > 0m)
            {
                movements.Add(new Movement(
                    document.InvoiceDate.Date, 3, "Collected", number,
                    "On the invoice, no receipt row", 0m, collected));
            }
        }

        var ordered = movements
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Order)
            .ThenBy(m => m.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<CustomerStatementLine>(ordered.Count);
        var running = 0m;
        foreach (var movement in ordered)
        {
            running = Math.Round(running + movement.Debit - movement.Credit, 2);
            lines.Add(new CustomerStatementLine(
                movement.Date,
                movement.Kind,
                movement.Reference,
                movement.Detail,
                movement.Debit,
                movement.Credit,
                running));
        }

        var name = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName.Trim();
        return new CustomerStatement(customerId, name, cutOff, lines, running);
    }

    private static bool HasLiveReceipt(CustomerStatementDocument document)
    {
        if (document.Payments == null)
            return false;

        foreach (var payment in document.Payments)
        {
            if (!payment.IsDeleted && payment.Amount != 0m)
                return true;
        }

        return false;
    }

    private static string DocumentDetail(InvoiceDocumentType type) => type switch
    {
        InvoiceDocumentType.Deposit => "Deposit, VAT-inclusive",
        InvoiceDocumentType.Partial => "Progress, VAT-inclusive",
        InvoiceDocumentType.Final => "Final, VAT-inclusive",
        _ => "VAT-inclusive"
    };

    private readonly record struct Movement(
        DateTime Date,
        int Order,
        string Kind,
        string Reference,
        string Detail,
        decimal Debit,
        decimal Credit);
}
