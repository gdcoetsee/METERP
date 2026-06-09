namespace METERP.Domain;

/// <summary>
/// Status values for a Quote in the Quote -> Job workflow.
/// </summary>
public enum QuoteStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4
}
