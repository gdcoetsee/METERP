namespace METERP.Domain;

/// <summary>
/// Formal goods received voucher (GRV) — paperless receipt against a PO.
/// </summary>
public class GoodsReceiptVoucher : BaseEntity
{
    public string GrvNumber { get; set; } = string.Empty;

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public Guid? StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    public Guid ReceivedByUserId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public string? SupplierDeliveryNote { get; set; }

    public ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}