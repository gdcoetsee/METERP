namespace METERP.Domain;

public enum StockTakeStatus
{
    Open = 0,
    Posted = 1,
    Cancelled = 2
}

/// <summary>
/// Periodic stock take / cycle count session for a tenant.
/// </summary>
public class StockTakeSession : BaseEntity
{
    public string SessionNumber { get; set; } = string.Empty;

    public StockTakeStatus Status { get; set; } = StockTakeStatus.Open;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public Guid StartedByUserId { get; set; }

    public DateTime? PostedAt { get; set; }

    public Guid? PostedByUserId { get; set; }

    public string? Notes { get; set; }

    public ICollection<StockTakeLine> Lines { get; set; } = new List<StockTakeLine>();
}