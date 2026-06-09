namespace METERP.Domain;

/// <summary>
/// Purchase Order to Supplier (to replenish Inventory).
/// Links to Supplier + lines; receipt updates stock via StockTransaction (Receipt type).
/// </summary>
public class PurchaseOrder : BaseEntity
{
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string PoNumber { get; set; } = string.Empty;

    public DateTime PoDate { get; set; } = DateTime.UtcNow;

    public DateTime? ExpectedDate { get; set; }

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public decimal Subtotal { get; set; }

    public decimal TaxRate { get; set; } = 0.15m; // SA VAT default

    public decimal Tax { get; set; }

    public decimal Total { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
}
