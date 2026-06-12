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

    /// <summary>
    /// Primary crew lead / technician assigned to execute the job (scheduling).
    /// </summary>
    public Guid? AssignedEmployeeId { get; set; }
    public Employee? AssignedEmployee { get; set; }

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

    /// <summary>
    /// Computes the full actual cost for variance analysis.
    /// Uses tracked ActualCosts (Travel, Material, Other etc. - explicit travel is key) + active labor.
    /// Base ActualCost on the entity may hold initial values; tracked costs take precedence in this calc.
    /// Pure method for testability and reuse (no side effects).
    /// </summary>
    public decimal GetActualTotal() =>
        ActualCosts.Where(c => !c.IsDeleted).Sum(c => c.Amount) +
        Labors.Where(l => !l.IsDeleted).Sum(l => l.TotalCost);

    /// <summary>
    /// Variance = ActualTotal - QuotedTotal. Positive = over budget.
    /// </summary>
    public decimal GetVariance() => GetActualTotal() - QuotedTotal;
}
