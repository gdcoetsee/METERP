namespace METERP.Domain;

/// <summary>
/// A job / work order created from an accepted quote (or standalone).
/// Tracks execution and costs for the contracting company.
/// </summary>
public class Job : BaseEntity
{
    public Guid? QuoteId { get; set; }
    public Quote? Quote { get; set; }

    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? AssetId { get; set; }
    public Asset? Asset { get; set; }

    public string JobNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Scheduled;

    public DateTime? ScheduledStart { get; set; }

    public DateTime? CompletedDate { get; set; }

    /// <summary>
    /// Snapshot of the quote total at time of conversion.
    /// </summary>
    public decimal QuotedTotal { get; set; }

    /// <summary>
    /// Sum of actual costs recorded against the job.
    /// </summary>
    public decimal ActualCost { get; set; }

    public string? Notes { get; set; }

    public ICollection<JobCost> ActualCosts { get; set; } = new List<JobCost>();

    public ICollection<JobLabor> Labors { get; set; } = new List<JobLabor>();
}
