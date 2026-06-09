namespace METERP.Domain;

/// <summary>
/// Sales Order as intermediate between Quote and Job (per original roadmap).
/// Allows confirmation/fulfillment step before converting to execution (Job).
/// </summary>
public class SalesOrder : BaseEntity
{
    public Guid QuoteId { get; set; }
    public Quote? Quote { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string SoNumber { get; set; } = string.Empty;

    public DateTime SoDate { get; set; } = DateTime.UtcNow;

    public DateTime? DeliveryDate { get; set; }

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;

    public decimal Subtotal { get; set; }

    public decimal TaxRate { get; set; } = 0.15m;

    public decimal Tax { get; set; }

    public decimal Total { get; set; }

    public string? Notes { get; set; }

    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
}
