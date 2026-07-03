namespace METERP.Domain;

public class StockTakeLine : BaseEntity
{
    public Guid StockTakeSessionId { get; set; }
    public StockTakeSession? StockTakeSession { get; set; }

    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    public decimal SystemQuantity { get; set; }

    public decimal? CountedQuantity { get; set; }

    public decimal Variance => (CountedQuantity ?? SystemQuantity) - SystemQuantity;
}