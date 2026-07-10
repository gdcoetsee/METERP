namespace METERP.Domain;

/// <summary>
/// Supplier quote against a stock requisition shortfall (multi-supplier RFQ lite).
/// Manager selects one; PO is created from the selected quote's supplier.
/// </summary>
public class ProcurementSupplierQuote : BaseEntity
{
    public Guid StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public decimal QuotedTotal { get; set; }

    public string? Notes { get; set; }

    public DateTime QuotedAt { get; set; } = DateTime.UtcNow;

    public bool IsSelected { get; set; }

    public Guid? SelectedByUserId { get; set; }

    public DateTime? SelectedAt { get; set; }
}
