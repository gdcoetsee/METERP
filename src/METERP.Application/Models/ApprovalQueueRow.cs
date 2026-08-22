namespace METERP.Application.Models;

public sealed record ApprovalQueueRow(
    Guid Id,
    string Kind,
    string Number,
    string Subject,
    string Href,
    DateTime? WaitingSince,
    string Stage);
