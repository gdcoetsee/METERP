namespace METERP.Domain;

/// <summary>
/// Payment recorded against an invoice, optionally with proof-of-payment (POP) attachment.
/// </summary>
public class InvoicePayment : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public decimal Amount { get; set; }

    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    public string? Reference { get; set; }

    public string? PopStorageKey { get; set; }

    public string? PopFileName { get; set; }

    public string? PopContentType { get; set; }

    public Guid? RecordedByUserId { get; set; }

    public string? Notes { get; set; }
}