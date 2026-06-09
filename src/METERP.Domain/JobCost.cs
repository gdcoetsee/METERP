namespace METERP.Domain;

/// <summary>
/// Actual cost line recorded against a Job (for variance tracking vs quote).
/// </summary>
public class JobCost : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    /// <summary>
    /// Labour, Material, Travel, Equipment, Other, etc.
    /// </summary>
    public string CostType { get; set; } = "Other";

    public DateTime CostDate { get; set; } = DateTime.UtcNow;
}
