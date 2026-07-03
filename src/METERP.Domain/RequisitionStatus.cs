namespace METERP.Domain;

public enum RequisitionStatus
{
    PendingManager = 0,
    PendingExecutive = 1,
    Approved = 2,
    Issued = 3,
    Rejected = 4,
    Cancelled = 5,
    AwaitingProcurement = 6,
    ProcurementOrdered = 7
}