namespace METERP.Domain;

/// <summary>
/// Individual line item on a Quote (material, labour, etc.).
/// </summary>
public class QuoteLine : BaseEntity
{
    public Guid QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Optional unit of measure: ea, hr, m, kg, etc.
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Labour, Material, Other, etc.
    /// </summary>
    public string LineType { get; set; } = "Material";

    /// <summary>
    /// Optional link to inventory for auto material lines.
    /// </summary>
    public Guid? InventoryItemId { get; set; }

    /// <summary>
    /// Quantity * UnitPrice. Always computed for correctness (services no longer need to pre-set it).
    /// </summary>
    public decimal LineTotal => Quantity * UnitPrice;
}
