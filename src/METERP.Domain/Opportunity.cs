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

    /// <summary>Position within the pipeline lane (lower = higher on the board).</summary>
    public int BoardOrder { get; set; }

    public DateTime ExpectedClose { get; set; } = DateTime.UtcNow.AddDays(30);

    public string? Notes { get; set; }

    /// <summary>Sales owner — the person chasing this deal.</summary>
    public Guid? OwnerEmployeeId { get; set; }
    public Employee? OwnerEmployee { get; set; }

    /// <summary>Win likelihood 0–100. Defaults from <see cref="OpportunityPipeline.DefaultProbability"/>.</summary>
    public int ProbabilityPercent { get; set; }

    public OpportunityPriority Priority { get; set; } = OpportunityPriority.Medium;

    public OpportunitySource Source { get; set; } = OpportunitySource.Other;

    public OpportunityDealType DealType { get; set; } = OpportunityDealType.NewWork;

    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    public DateTime? NextFollowUp { get; set; }

    /// <summary>Required when stage is Closed Lost.</summary>
    public string? LossReason { get; set; }

    public DateTime? LastActivityAt { get; set; }

    /// <summary>Set when converted to a quote via AI Copilot or manual flow.</summary>
    public Guid? QuoteId { get; set; }
    public Quote? Quote { get; set; }

    public decimal WeightedValue => OpportunityPipeline.WeightedValue(Value, ProbabilityPercent);
}