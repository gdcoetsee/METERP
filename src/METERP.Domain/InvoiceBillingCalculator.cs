namespace METERP.Domain;

/// <summary>
/// Pure billing calculations for retention, balances, and payment status.
/// </summary>
public static class InvoiceBillingCalculator
{
    public static decimal CalculateRetentionAmount(decimal subtotal, decimal retentionPercent)
    {
        if (retentionPercent <= 0 || subtotal <= 0)
            return 0m;

        return Math.Round(subtotal * retentionPercent / 100m, 2);
    }

    public static decimal CalculateBalanceDue(decimal total, decimal amountPaid) =>
        Math.Max(0m, Math.Round(total - amountPaid, 2));

    public static decimal CalculateNetCollectable(decimal total, decimal retentionAmount, decimal amountPaid) =>
        Math.Max(0m, Math.Round(total - retentionAmount - amountPaid, 2));

    public static InvoiceStatus DerivePaymentStatus(
        decimal total,
        decimal amountPaid,
        InvoiceStatus current,
        DateTime dueDate,
        DateTime asOfUtc)
    {
        if (current == InvoiceStatus.Cancelled)
            return current;

        var balance = CalculateBalanceDue(total, amountPaid);
        if (balance <= 0)
            return InvoiceStatus.Paid;

        if (amountPaid > 0)
            return InvoiceStatus.PartiallyPaid;

        if (current == InvoiceStatus.Draft)
            return InvoiceStatus.Draft;

        if (asOfUtc.Date > dueDate.Date)
            return InvoiceStatus.Overdue;

        return current is InvoiceStatus.Sent or InvoiceStatus.Overdue or InvoiceStatus.PartiallyPaid
            ? InvoiceStatus.Sent
            : current;
    }

    public static int GetDaysOverdue(DateTime dueDate, DateTime asOfUtc)
    {
        var days = (asOfUtc.Date - dueDate.Date).Days;
        return days > 0 ? days : 0;
    }

    public static string GetAgingBucket(int daysOverdue) => daysOverdue switch
    {
        <= 0 => "Current",
        <= 30 => "1-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "90+"
    };

    /// <summary>
    /// Proforma, draft, cancelled, and credit notes do not count as money billed against a job.
    /// </summary>
    public static bool CountsTowardJobBilled(InvoiceDocumentType type, InvoiceStatus status) =>
        type is not (InvoiceDocumentType.Proforma or InvoiceDocumentType.CreditNote)
        && status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled);

    public static decimal CalculateUnbilledResidual(decimal quotedTotal, decimal billedToDate) =>
        Math.Max(0m, Math.Round(quotedTotal - billedToDate, 2));

    /// <summary>
    /// Leftover quote is material when it is more than 10% of quoted and at least 100.
    /// Close may proceed, but the executive must write notes acknowledging it.
    /// </summary>
    public static bool RequiresUnbilledCloseAcknowledgement(decimal quotedTotal, decimal billedToDate)
    {
        var residual = CalculateUnbilledResidual(quotedTotal, billedToDate);
        if (residual < 100m)
            return false;
        if (quotedTotal <= 0)
            return true;
        return residual > quotedTotal * 0.10m;
    }
}