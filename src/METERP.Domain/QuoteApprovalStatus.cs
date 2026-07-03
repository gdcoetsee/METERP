namespace METERP.Domain;

/// <summary>
/// Executive governance gate before a quote may be sent to the client.
/// </summary>
public enum QuoteApprovalStatus
{
    None = 0,
    PendingExecutive = 1,
    ExecutiveApproved = 2,
    Rejected = 3
}