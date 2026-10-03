namespace METERP.Domain;

/// <summary>
/// Customer invoice, typically created from a completed Job.
/// Supports the full Quote -> Job -> Invoice flow for contracting work.
/// </summary>
public class Invoice : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? JobId { get; set; }
    public Job? Job { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(30);

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public InvoiceDocumentType DocumentType { get; set; } = InvoiceDocumentType.Standard;

    /// <summary>Source invoice when this document is a credit note. Null means an unlinked credit note.</summary>
    public Guid? CreditNoteForInvoiceId { get; set; }
    public Invoice? CreditNoteForInvoice { get; set; }

    /// <summary>Retention withheld (typically % of subtotal) until practical completion.</summary>
    public decimal RetentionPercent { get; set; }

    public decimal RetentionAmount { get; set; }

    public decimal AmountPaid { get; set; }

    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TaxRate { get; set; } = 0.15m;

    public decimal Tax { get; set; }

    /// <summary>
    /// Document total, stored positive for every type including credit notes.
    /// See <see cref="InvoiceCreditConvention"/>: a credit note is identified by
    /// <see cref="InvoiceDocumentType.CreditNote"/>, shown in parentheses, and subtracted in AR.
    /// </summary>
    public decimal Total { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();

    public ICollection<InvoicePayment> Payments { get; set; } = new List<InvoicePayment>();

    public decimal BalanceDue => InvoiceBillingCalculator.CalculateBalanceDue(Total, AmountPaid);

    public decimal NetCollectable => InvoiceBillingCalculator.CalculateNetCollectable(Total, RetentionAmount, AmountPaid);

    /// <summary>
    /// Recalculates Subtotal, Tax and Total from non-deleted lines.
    /// This is the source of truth for invoice pricing (moved to Domain for testability and correctness).
    /// Credit notes keep a positive total — see <see cref="InvoiceCreditConvention"/>.
    /// </summary>
    public void RecalculateTotals()
    {
        Subtotal = Lines.Where(l => !l.IsDeleted).Sum(l => l.LineTotal);
        Tax = Math.Round(Subtotal * TaxRate, 2);
        Total = Subtotal + Tax;
        RetentionAmount = InvoiceBillingCalculator.CalculateRetentionAmount(Subtotal, RetentionPercent);
        if (DocumentType == InvoiceDocumentType.CreditNote && Total < 0m)
        {
            Subtotal = Math.Abs(Subtotal);
            Tax = Math.Abs(Tax);
            Total = Math.Abs(Total);
        }
    }
}
