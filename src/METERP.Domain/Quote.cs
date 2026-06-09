namespace METERP.Domain;

/// <summary>
/// A quote/estimate sent to a customer for work (materials + labour).
/// Part of the core Quote -> Job workflow for contracting.
/// </summary>
public class Quote : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>
    /// Human readable quote reference (e.g. Q-2026-0042).
    /// </summary>
    public string QuoteNumber { get; set; } = string.Empty;

    public DateTime QuoteDate { get; set; } = DateTime.UtcNow;

    public DateTime ValidUntil { get; set; } = DateTime.UtcNow.AddDays(30);

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public string? Notes { get; set; }

    /// <summary>
    /// Subtotal before tax (sum of line totals).
    /// </summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// Tax rate e.g. 0.15 for South Africa VAT.
    /// </summary>
    public decimal TaxRate { get; set; } = 0.15m;

    public decimal Tax { get; set; }

    public decimal Total { get; set; }

    public ICollection<QuoteLine> Lines { get; set; } = new List<QuoteLine>();
}
