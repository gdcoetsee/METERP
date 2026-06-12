namespace METERP.Domain;

/// <summary>
/// CRM opportunity in the sales pipeline (Opportunity → Quote spine entry).
/// </summary>
public class Opportunity : BaseEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional link to an existing customer; CustomerName used when not linked yet.</summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string? CustomerName { get; set; }

    public decimal Value { get; set; }

    public OpportunityStage Stage { get; set; } = OpportunityStage.Lead;

    public DateTime ExpectedClose { get; set; } = DateTime.UtcNow.AddDays(30);

    public string? Notes { get; set; }

    /// <summary>Set when converted to a quote via AI Copilot or manual flow.</summary>
    public Guid? QuoteId { get; set; }
    public Quote? Quote { get; set; }
}