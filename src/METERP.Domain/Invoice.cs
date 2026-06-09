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

    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }

    public decimal TaxRate { get; set; } = 0.15m;

    public decimal Tax { get; set; }

    public decimal Total { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
