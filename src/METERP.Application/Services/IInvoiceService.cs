using METERP.Application.Models;
using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Service for customer invoicing, completing the Quote -> Job -> Invoice flow.
/// </summary>
public interface IInvoiceService
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Invoice>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default,
        bool unlinkedOnly = false,
        bool unlinkedCreditsOnly = false);

    /// <summary>
    /// Sets <see cref="Invoice.JobId"/> on an invoice that has no job.
    /// Does not change invoice totals, invoice status, or the job's status.
    /// </summary>
    Task LinkJobAsync(Guid invoiceId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Sets <see cref="Invoice.CreditNoteForInvoiceId"/> on an unlinked credit note.
    /// Does not change totals. The parent must be a sales invoice for the same customer.
    /// </summary>
    Task LinkCreditNoteParentAsync(Guid creditNoteId, string parentInvoiceNumber, CancellationToken ct = default);

    Task<Guid> CreateAsync(Invoice invoice, CancellationToken ct = default);
    Task UpdateAsync(Invoice invoice, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Line management (consistent with Quotes)
    Task<Guid> AddLineAsync(InvoiceLine line, CancellationToken ct = default);
    Task UpdateLineAsync(InvoiceLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Creates an invoice from a completed job. Snapshots totals and (if available) line items from the originating quote.
    /// </summary>
    Task<Invoice> CreateFromJobAsync(Guid jobId, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid invoiceId, InvoiceStatus newStatus, CancellationToken ct = default);

    Task<IReadOnlyList<InvoicePayment>> GetPaymentsAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Records a payment. Emails the customer a receipt when SMTP and a customer email are set;
    /// the payment still succeeds if email cannot be sent.
    /// </summary>
    Task<Guid> RecordPaymentAsync(
        Guid invoiceId,
        decimal amount,
        DateTime paymentDate,
        string? reference,
        Guid? recordedByUserId,
        string? notes,
        CancellationToken ct = default);

    Task<Guid> RecordPaymentWithPopAsync(
        Guid invoiceId,
        decimal amount,
        DateTime paymentDate,
        string? reference,
        string fileName,
        Stream popContent,
        string contentType,
        Guid? recordedByUserId,
        string? notes,
        CancellationToken ct = default);

    /// <summary>Opens POP attachment for a payment, if stored.</summary>
    Task<(Stream Content, string FileName, string ContentType)?> OpenPaymentPopAsync(
        Guid paymentId,
        CancellationToken ct = default);

    /// <summary>
    /// Undoes one receipt. Requires a reason. Reduces <see cref="Invoice.AmountPaid"/> and
    /// restores Sent, PartiallyPaid, or Overdue through <see cref="InvoiceBillingCalculator.DerivePaymentStatus"/>.
    /// A second reversal of the same receipt fails. Another tenant's receipt cannot be reversed.
    /// A deposit that is no longer fully paid clears <see cref="Job.DepositReceived"/> when no other counting deposit remains.
    /// </summary>
    Task ReversePaymentAsync(Guid paymentId, string reason, CancellationToken ct = default);

    Task<Invoice> CreateCreditNoteAsync(Guid sourceInvoiceId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Credits a VAT-inclusive rand amount, or a percent of the source total, as one line.
    /// Split with <see cref="InvoiceBillingCalculator.SplitVatInclusive"/> at the source tax rate.
    /// Refuses an amount above the source balance due. Reason rules match a full credit note.
    /// Amount and percent are mutually exclusive. A full-invoice credit still uses <see cref="CreateCreditNoteAsync"/>.
    /// </summary>
    Task<Invoice> CreatePartialCreditNoteAsync(
        Guid sourceInvoiceId,
        string reason,
        decimal? inclusiveAmount,
        decimal? percentOfTotal,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a draft credit note as <see cref="InvoiceStatus.Sent"/> so it reduces the customer statement.
    /// Totals are unchanged. Refuses a proforma, a cancelled invoice, and a credit note whose source is
    /// another credit note, a proforma, or a cancelled invoice. Tenant-scoped. Does not require an email.
    /// </summary>
    Task<Invoice> IssueCreditNoteAsync(Guid creditNoteId, CancellationToken ct = default);

    Task<Invoice> CreateBillingDocumentAsync(
        Guid jobId,
        InvoiceDocumentType documentType,
        decimal? percentOfQuotedTotal = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgedDebtorRow>> GetAgedDebtorsAsync(CancellationToken ct = default);

    /// <summary>
    /// Account statement for one customer. Null when the customer is not in this tenant.
    /// Totals are the stored VAT-inclusive amounts. See <see cref="CustomerStatementBuilder"/>.
    /// </summary>
    Task<CustomerStatement?> GetCustomerStatementAsync(
        Guid customerId,
        DateTime? asOfUtc = null,
        CancellationToken ct = default);

    /// <summary>Draft invoices that have lines and can be marked Sent.</summary>
    Task<IReadOnlyList<ConvertibleDocumentRow>> GetUnsentQueueAsync(int take = 20, CancellationToken ct = default);

    /// <summary>
    /// Sends a payment reminder for an overdue invoice with a remaining balance.
    /// Emails the customer when SMTP is configured; always audits the chase.
    /// </summary>
    Task<InvoiceChaseResult> ChaseOverdueAsync(Guid invoiceId, CancellationToken ct = default);
}
