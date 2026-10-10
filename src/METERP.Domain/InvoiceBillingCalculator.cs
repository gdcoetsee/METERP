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

    /// <summary>
    /// Splits a VAT-inclusive rand amount into ex-VAT and VAT.
    /// Access totals were split once this way. The office figure stays the gross amount.
    /// </summary>
    public static (decimal Subtotal, decimal Tax) SplitVatInclusive(decimal total, decimal rate = 0.15m)
    {
        if (total == 0 || rate <= 0)
            return (total, 0m);

        var subtotal = Math.Round(total / (1 + rate), 2, MidpointRounding.AwayFromZero);
        var tax = Math.Round(total - subtotal, 2, MidpointRounding.AwayFromZero);
        return (subtotal, tax);
    }

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

        // Paid falls back to Sent once the receipt is gone and the due date has not passed.
        return current is InvoiceStatus.Sent or InvoiceStatus.Overdue or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid
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
    /// Open credit notes still reduce customer AR — see <see cref="InvoiceCreditConvention"/>.
    /// </summary>
    public static bool CountsTowardJobBilled(InvoiceDocumentType type, InvoiceStatus status) =>
        type is not (InvoiceDocumentType.Proforma or InvoiceDocumentType.CreditNote)
        && status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled);

    public static decimal CalculateUnbilledResidual(decimal quotedTotal, decimal billedToDate) =>
        Math.Max(0m, Math.Round(quotedTotal - billedToDate, 2));

    /// <summary>QuotedTotal * DepositPercent / 100, rounded to cents. Zero when either input is not positive.</summary>
    public static decimal CalculateDepositThreshold(decimal quotedTotal, decimal depositPercent)
    {
        if (quotedTotal <= 0m || depositPercent <= 0m)
            return 0m;

        return Math.Round(quotedTotal * depositPercent / 100m, 2);
    }

    public static bool BilledCoversDeposit(decimal quotedTotal, decimal depositPercent, decimal billedToDate)
    {
        var due = CalculateDepositThreshold(quotedTotal, depositPercent);
        return due > 0m && billedToDate >= due;
    }

    /// <summary>
    /// One job invoice used to decide whether the deposit is still covered.
    /// </summary>
    public readonly record struct BillingDocumentSlice(
        Guid Id,
        InvoiceDocumentType DocumentType,
        InvoiceStatus Status,
        decimal Total,
        Guid? CreditNoteForInvoiceId);

    /// <summary>
    /// Billed cash net of issued credit notes, and whether a deposit document
    /// still counts (no issued credit note is linked to it).
    /// </summary>
    public readonly record struct DepositCoverSnapshot(decimal BilledCover, bool HasCountingDeposit);

    /// <summary>
    /// Issued credit notes reduce billed cover. A draft or cancelled credit does not.
    /// A deposit document stops counting once an issued credit note is linked to it.
    /// </summary>
    public static DepositCoverSnapshot SummarizeDepositCover(IEnumerable<BillingDocumentSlice> documents)
    {
        var billed = 0m;
        var credits = 0m;
        var creditedDeposits = new HashSet<Guid>();
        var countingDeposits = new List<Guid>();

        foreach (var doc in documents)
        {
            if (CountsTowardJobBilled(doc.DocumentType, doc.Status))
            {
                billed += doc.Total;
                if (doc.DocumentType == InvoiceDocumentType.Deposit)
                    countingDeposits.Add(doc.Id);
            }
            else if (doc.DocumentType == InvoiceDocumentType.CreditNote
                && doc.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled))
            {
                var amount = Math.Abs(doc.Total);
                credits += amount;
                if (doc.CreditNoteForInvoiceId is Guid parent && amount > 0m)
                    creditedDeposits.Add(parent);
            }
        }

        var hasCountingDeposit = false;
        foreach (var id in countingDeposits)
        {
            if (!creditedDeposits.Contains(id))
            {
                hasCountingDeposit = true;
                break;
            }
        }

        return new DepositCoverSnapshot(
            Math.Round(billed - credits, 2, MidpointRounding.AwayFromZero),
            hasCountingDeposit);
    }

    /// <summary>
    /// Catch the job flag up to linked invoices. A counting deposit document, or billed cash
    /// that already meets the deposit threshold, is enough — no separate money column.
    /// Pass billed cash net of issued credit notes (<see cref="DepositCoverSnapshot.BilledCover"/>).
    /// Pass false for <paramref name="hasCountingDepositInvoice"/> when an issued credit note
    /// is already linked to that deposit. This only turns the flag on. Clearing is
    /// <see cref="ShouldClearDepositReceivedAfterCredit"/>.
    /// </summary>
    public static bool ShouldSoftSyncDepositReceived(
        bool depositReceived,
        decimal quotedTotal,
        decimal depositPercent,
        decimal billedToDate,
        bool hasCountingDepositInvoice)
    {
        if (depositReceived || depositPercent <= 0m)
            return false;

        if (hasCountingDepositInvoice)
            return true;

        return BilledCoversDeposit(quotedTotal, depositPercent, billedToDate);
    }

    /// <summary>
    /// An issued credit clears <see cref="Job.DepositReceived"/> when the job is still open
    /// and soft-sync would not turn the flag back on. A full credit of the only counting
    /// deposit drops billed cover below the threshold and clears the flag. A partial credit
    /// that leaves billed cover at or above the threshold does not. Closed and cancelled
    /// jobs are unchanged. A draft credit does not change the snapshot, so it does not clear.
    /// </summary>
    public static bool ShouldClearDepositReceivedAfterCredit(
        bool depositReceived,
        JobStatus jobStatus,
        decimal quotedTotal,
        decimal depositPercent,
        decimal billedCover,
        bool hasCountingDeposit)
    {
        if (!depositReceived)
            return false;

        if (jobStatus is JobStatus.Closed or JobStatus.Cancelled)
            return false;

        return !ShouldSoftSyncDepositReceived(
            false,
            quotedTotal,
            depositPercent,
            billedCover,
            hasCountingDeposit);
    }

    /// <summary>
    /// A reversed deposit receipt clears <see cref="Job.DepositReceived"/> only when this
    /// deposit is no longer fully paid and no other counting deposit document remains.
    /// </summary>
    public static bool ShouldClearDepositReceived(
        bool depositReceived,
        InvoiceDocumentType reversedDocumentType,
        decimal invoiceTotal,
        decimal amountPaidAfterReversal,
        bool otherCountingDepositRemains)
    {
        if (!depositReceived || reversedDocumentType != InvoiceDocumentType.Deposit)
            return false;

        if (amountPaidAfterReversal >= invoiceTotal)
            return false;

        return !otherCountingDepositRemains;
    }

    /// <summary>
    /// Job Command Center "Raise deposit" banner.
    /// Live ops jobs only (Helm). Also hidden when billed already covers the deposit,
    /// or when a completed/closed job has any billed amount.
    /// </summary>
    public static bool ShowDepositCollectionBanner(
        JobStatus status,
        decimal depositPercent,
        bool depositReceived,
        decimal quotedTotal,
        decimal billedToDate)
    {
        if (depositReceived || depositPercent <= 0m)
            return false;

        if (status is JobStatus.Completed or JobStatus.Closed && billedToDate > 0m)
            return false;

        if (BilledCoversDeposit(quotedTotal, depositPercent, billedToDate))
            return false;

        return status is JobStatus.Scheduled or JobStatus.InProgress or JobStatus.OnHold;
    }

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