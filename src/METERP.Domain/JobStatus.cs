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
    Invoiced = 4
}
