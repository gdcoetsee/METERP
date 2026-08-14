namespace METERP.Application.Models;

public sealed record ApprovalQueueRow(
    string Kind,
    string Number,
    string Subject,
    string Href,
    DateTime? WaitingSince);
