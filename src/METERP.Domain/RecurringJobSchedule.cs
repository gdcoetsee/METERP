namespace METERP.Domain;

/// <summary>
/// Recurring maintenance / SLA job template — spawns jobs on interval.
/// </summary>
public class RecurringJobSchedule : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? DivisionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int IntervalDays { get; set; } = 30;

    public DateTime NextRunDate { get; set; } = DateTime.UtcNow.Date;

    public decimal DefaultQuotedTotal { get; set; }

    public bool IsActive { get; set; } = true;
}