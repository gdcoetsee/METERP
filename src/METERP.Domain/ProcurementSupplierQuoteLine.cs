namespace METERP.Domain;

/// <summary>
/// Line-level supplier quote against a requisition shortfall (RFQ depth).
/// When present, header <see cref="ProcurementSupplierQuote.QuotedTotal"/> is the sum of line totals.
/// </summary>
public class ProcurementSupplierQuoteLine : BaseEntity
{
    public Guid ProcurementSupplierQuoteId { get; set; }
    public ProcurementSupplierQuote? Quote { get; set; }

    /// <summary>Optional link to the requisition line being priced.</summary>
    public Guid? StockRequisitionLineId { get; set; }
    public StockRequisitionLine? StockRequisitionLine { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
