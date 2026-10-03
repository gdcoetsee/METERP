namespace METERP.Domain;

/// <summary>
/// One credit-note sign convention. Credit notes store Subtotal, Tax, and Total as positive amounts.
/// <see cref="InvoiceDocumentType.CreditNote"/> is what makes the document a credit.
/// Screens show the amount as a credit (parentheses). Accounts receivable, customer balance,
/// and aged debtors subtract that positive total. Do not store some credit notes negative and others positive.
/// </summary>
public static class InvoiceCreditConvention
{
    public static bool IsCreditNote(InvoiceDocumentType type) =>
        type == InvoiceDocumentType.CreditNote;

    /// <summary>List and detail amount. Standard invoices stay "R 1,234.56". Credits are "(R 1,234.56)".</summary>
    public static string FormatDocumentAmount(InvoiceDocumentType type, decimal amount)
    {
        if (IsCreditNote(type))
            return $"(R {Math.Abs(amount):N2})";

        return $"R {amount:N2}";
    }

    /// <summary>Open balance. A remaining credit is shown in parentheses. A settled credit is R 0.00.</summary>
    public static string FormatDocumentBalance(InvoiceDocumentType type, decimal balanceDue)
    {
        if (IsCreditNote(type) && balanceDue > 0m)
            return $"(R {balanceDue:N2})";

        return $"R {balanceDue:N2}";
    }

    /// <summary>Customer statement balance. Negative means the customer is in credit.</summary>
    public static string FormatCustomerBalance(decimal signedBalance)
    {
        if (signedBalance < 0m)
            return $"(R {Math.Abs(signedBalance):N2})";

        return $"R {signedBalance:N2}";
    }

    /// <summary>
    /// Signed effect on open AR. Sales documents add their balance due.
    /// An open credit note subtracts its remaining positive total.
    /// Draft, proforma, cancelled, and already-settled (paid) credits contribute nothing.
    /// </summary>
    public static decimal ArSignedOpenBalance(
        InvoiceDocumentType type,
        InvoiceStatus status,
        decimal total,
        decimal amountPaid)
    {
        if (type == InvoiceDocumentType.Proforma || status is InvoiceStatus.Draft or InvoiceStatus.Cancelled)
            return 0m;

        if (IsCreditNote(type))
        {
            if (status == InvoiceStatus.Paid)
                return 0m;

            return -OpenCreditMagnitude(total, amountPaid);
        }

        return InvoiceBillingCalculator.CalculateBalanceDue(total, amountPaid);
    }

    /// <summary>Positive amount of an open credit still to apply. Storage may be positive or (legacy) negative.</summary>
    public static decimal OpenCreditMagnitude(decimal total, decimal amountPaid) =>
        Math.Max(0m, Math.Round(Math.Abs(total) - Math.Abs(amountPaid), 2));

    /// <summary>Spreadsheet sign: credits are negative so a column sums to the AR effect. Storage stays positive.</summary>
    public static decimal SignedStoredTotal(InvoiceDocumentType type, decimal total) =>
        IsCreditNote(type) ? -Math.Abs(total) : total;

    public readonly record struct AgedInvoiceSlice(Guid InvoiceId, Guid CustomerId, DateTime DueDate, decimal Balance);

    public readonly record struct OpenCredit(Guid CustomerId, Guid? ParentInvoiceId, decimal Magnitude);

    /// <summary>
    /// Reduces overdue invoice balances by open credits.
    /// A linked credit is applied to its parent first. Anything still unapplied, and every unlinked credit,
    /// reduces that customer's oldest overdue balances. Standard invoices with no credit are unchanged.
    /// </summary>
    public static Dictionary<Guid, decimal> NetAgedBalances(
        IReadOnlyList<AgedInvoiceSlice> invoices,
        IReadOnlyList<OpenCredit> credits)
    {
        var balances = invoices.ToDictionary(i => i.InvoiceId, i => Math.Max(0m, i.Balance));
        var unallocated = new List<(Guid CustomerId, decimal Amount)>();

        foreach (var credit in credits)
        {
            if (credit.Magnitude <= 0m)
                continue;

            var left = credit.Magnitude;
            if (credit.ParentInvoiceId is Guid parentId && balances.TryGetValue(parentId, out var parentBalance) && parentBalance > 0m)
            {
                var applied = Math.Min(parentBalance, left);
                balances[parentId] = Math.Round(parentBalance - applied, 2);
                left = Math.Round(left - applied, 2);
            }

            if (left > 0m)
                unallocated.Add((credit.CustomerId, left));
        }

        foreach (var group in unallocated.GroupBy(x => x.CustomerId))
        {
            var left = group.Sum(x => x.Amount);
            foreach (var invoice in invoices
                         .Where(i => i.CustomerId == group.Key)
                         .OrderBy(i => i.DueDate)
                         .ThenBy(i => i.InvoiceId))
            {
                if (left <= 0m)
                    break;

                var balance = balances[invoice.InvoiceId];
                if (balance <= 0m)
                    continue;

                var applied = Math.Min(balance, left);
                balances[invoice.InvoiceId] = Math.Round(balance - applied, 2);
                left = Math.Round(left - applied, 2);
            }
        }

        return balances;
    }

    /// <summary>
    /// Flips a legacy negative credit note onto the positive-total convention.
    /// Returns false when the document is already stored positive.
    /// </summary>
    public static bool NormalizeStoredPositive(Invoice invoice)
    {
        if (!IsCreditNote(invoice.DocumentType))
            return false;

        var changed = false;
        if (invoice.AmountPaid < 0m)
        {
            invoice.AmountPaid = Math.Abs(invoice.AmountPaid);
            changed = true;
        }

        foreach (var line in invoice.Lines.Where(l => !l.IsDeleted))
        {
            if (line.UnitPrice < 0m)
            {
                line.UnitPrice = Math.Abs(line.UnitPrice);
                changed = true;
            }

            if (line.Quantity < 0m)
            {
                line.Quantity = Math.Abs(line.Quantity);
                changed = true;
            }
        }

        if (changed)
        {
            invoice.RecalculateTotals();
            if (invoice.Total < 0m)
            {
                invoice.Subtotal = Math.Abs(invoice.Subtotal);
                invoice.Tax = Math.Abs(invoice.Tax);
                invoice.Total = Math.Abs(invoice.Total);
            }

            return true;
        }

        if (invoice.Total < 0m || invoice.Subtotal < 0m || invoice.Tax < 0m)
        {
            invoice.Subtotal = Math.Abs(invoice.Subtotal);
            invoice.Tax = Math.Abs(invoice.Tax);
            invoice.Total = Math.Abs(invoice.Total);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Access sold/completed credits were left Sent because SignedAmount was negative.
    /// Those documents are settled: mark them paid so they do not reduce today's open debt.
    /// </summary>
    public static bool SettleImportedCredit(Invoice invoice)
    {
        if (!IsCreditNote(invoice.DocumentType))
            return false;

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.Overdue))
            return false;

        if (invoice.AmountPaid != 0m)
            return false;

        if (!NotesMarkAccessSettled(invoice.Notes))
            return false;

        invoice.Status = InvoiceStatus.Paid;
        invoice.AmountPaid = Math.Abs(invoice.Total);
        return true;
    }

    public static bool NotesMarkAccessSettled(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return false;

        return notes.Contains("Status=Sold", StringComparison.OrdinalIgnoreCase)
            || notes.Contains("Status=Completed AIP", StringComparison.OrdinalIgnoreCase)
            || notes.Contains("Status=TRF stock completed", StringComparison.OrdinalIgnoreCase);
    }
}
