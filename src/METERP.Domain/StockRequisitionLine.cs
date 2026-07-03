namespace METERP.Domain;

public class StockRequisitionLine : BaseEntity
{
    public Guid StockRequisitionId { get; set; }
    public StockRequisition? StockRequisition { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public decimal QuantityRequested { get; set; }

    public decimal QuantityReserved { get; set; }

    public decimal QuantityIssued { get; set; }
}