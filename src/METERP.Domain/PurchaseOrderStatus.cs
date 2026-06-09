namespace METERP.Domain;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Sent = 1,
    PartiallyReceived = 2,
    Received = 3,
    Cancelled = 4
}
