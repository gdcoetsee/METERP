using METERP.Domain;

namespace METERP.Common;

/// <summary>
/// In-memory quote line for the UI before the quote is persisted (needs stable draft id for edit/delete).
/// </summary>
public sealed class QuoteLineDraft
{
    public Guid DraftId { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitCost { get; set; }
    public decimal GrossProfitPercent { get; set; } = 0.25m;
    public decimal UnitPrice { get; set; }
    public string? Unit { get; set; }
    public string LineType { get; set; } = "Material";

    public decimal LineTotal => Quantity * UnitPrice;

    public QuoteLine ToQuoteLine(Guid quoteId = default) => new()
    {
        QuoteId = quoteId,
        Description = Description,
        Quantity = Quantity,
        UnitCost = UnitCost,
        GrossProfitPercent = GrossProfitPercent,
        UnitPrice = UnitPrice,
        Unit = Unit,
        LineType = LineType
    };
}