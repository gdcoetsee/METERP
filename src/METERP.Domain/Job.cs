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

    public Guid? DivisionId { get; set; }
    public Division? Division { get; set; }

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

    public DateTime? ClosedAt { get; set; }

    public Guid? ClosedByUserId { get; set; }

    public string? CloseNotes { get; set; }

    public DateTime? LastReopenedAt { get; set; }

    public Guid? LastReopenedByUserId { get; set; }

    public string? LastReopenReason { get; set; }

    public JobSignOffStatus SignOffStatus { get; set; } = JobSignOffStatus.None;

    /// <summary>Manager work sign-off (first stage).</summary>
    public DateTime? ManagerSignedOffAt { get; set; }

    public Guid? ManagerSignedOffByUserId { get; set; }

    /// <summary>Executive work sign-off (second stage — full SignedOff).</summary>
    public DateTime? SignedOffAt { get; set; }

    public Guid? SignedOffByUserId { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Guid? CancelledByUserId { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>Retention % applied on final invoices (e.g. 10 for construction).</summary>
    public decimal RetentionPercent { get; set; } = 10m;

    /// <summary>Deposit % required before mobilisation (informational for deposit invoices).</summary>
    public decimal DepositPercent { get; set; } = 30m;

    public bool DepositReceived { get; set; }

    /// <summary>
    /// Emergency / callout job created without a quote (job-first billing path).
    /// </summary>
    public bool IsEmergency { get; set; }

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

    public ICollection<JobCrewAssignment> CrewAssignments { get; set; } = new List<JobCrewAssignment>();

    public ICollection<JobMilestone> Milestones { get; set; } = new List<JobMilestone>();

    public ICollection<JobSnagItem> SnagItems { get; set; } = new List<JobSnagItem>();

    public ICollection<JobSafetyIncident> SafetyIncidents { get; set; } = new List<JobSafetyIncident>();

    /// <summary>Active crew members (excludes soft-deleted assignments).</summary>
    public IEnumerable<Employee> GetCrewEmployees() =>
        CrewAssignments
            .Where(c => !c.IsDeleted && c.Employee != null)
            .Select(c => c.Employee!);

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

    /// <summary>
    /// Budget margin percent: positive = under budget (favorable). Zero quoted returns 0.
    /// </summary>
    public decimal GetMarginPercent()
    {
        if (QuotedTotal <= 0)
            return 0m;

        return Math.Round((QuotedTotal - GetActualTotal()) / QuotedTotal * 100m, 1);
    }

    public bool IsClosed() => Status == JobStatus.Closed;

    public bool IsCancelled() => Status == JobStatus.Cancelled;

    public bool IsOpenForOperations() =>
        Status is not (JobStatus.Closed or JobStatus.Cancelled);

    /// <summary>Final/partial invoice cue: full dual work sign-off and job not closed/cancelled.</summary>
    public bool IsReadyToInvoice() =>
        SignOffStatus == JobSignOffStatus.SignedOff && IsOpenForOperations();

    public int GetProgressPercent()
    {
        var milestones = Milestones.Where(m => !m.IsDeleted).ToList();
        if (milestones.Count > 0)
            return (int)Math.Round(milestones.Average(m => m.PercentComplete));

        return Status switch
        {
            JobStatus.Scheduled => 10,
            JobStatus.InProgress => 50,
            JobStatus.OnHold => 40,
            JobStatus.Completed => 90,
            JobStatus.Invoiced => 90,
            JobStatus.Closed => 100,
            JobStatus.Cancelled => 0,
            _ => 0
        };
    }
}
