namespace METERP.Domain;

public class GoodsReceiptLine : BaseEntity
{
    public Guid GoodsReceiptVoucherId { get; set; }
    public GoodsReceiptVoucher? GoodsReceiptVoucher { get; set; }

    public Guid PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    public Guid? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public decimal QuantityReceived { get; set; }
}