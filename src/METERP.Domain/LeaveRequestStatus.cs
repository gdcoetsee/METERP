namespace METERP.Domain;

public enum LeaveRequestStatus
{
    PendingManager = 0,
    PendingExecutive = 1,
    PendingHr = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5
}