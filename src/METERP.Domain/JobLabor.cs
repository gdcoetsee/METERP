namespace METERP.Domain;

/// <summary>
/// Labor time entry against a Job (for timesheet / job costing).
/// Separate from material costs tracked via Inventory + JobCost.
/// </summary>
public class JobLabor : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;

    public DateTime WorkDate { get; set; } = DateTime.UtcNow.Date;

    public decimal Hours { get; set; }

    public decimal HourlyRate { get; set; }

    public string? Description { get; set; }

    public string? Technician { get; set; } // Could link to ApplicationUser later

    public decimal TotalCost => Hours * HourlyRate;
}
