namespace METERP.Domain;

/// <summary>
/// Line item on a customer Invoice (can be pulled from job/quote or added manually).
/// </summary>
public class InvoiceLine : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1m;

    public decimal UnitPrice { get; set; }

    public string? Unit { get; set; }

    public string LineType { get; set; } = "Material";

    public decimal LineTotal => Quantity * UnitPrice;
}
