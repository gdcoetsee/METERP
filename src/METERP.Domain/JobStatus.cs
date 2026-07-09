namespace METERP.Domain;

/// <summary>
/// Status values for a Job / work order.
/// </summary>
public enum JobStatus
{
    Scheduled = 0,
    InProgress = 1,
    OnHold = 2,
    Completed = 3,
    /// <summary>Legacy billing marker — no longer terminal; prefer <see cref="Completed"/> while job is open for costs.</summary>
    Invoiced = 4,
    Closed = 5,
    Cancelled = 6
}
