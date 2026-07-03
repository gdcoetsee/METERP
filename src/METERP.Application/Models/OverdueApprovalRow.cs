namespace METERP.Application.Models;

public sealed class OverdueApprovalRow
{
    public string ItemType { get; init; } = string.Empty;

    public string Reference { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateTime SubmittedAtUtc { get; init; }

    public int HoursInQueue { get; init; }

    public int SlaHours { get; init; }
}