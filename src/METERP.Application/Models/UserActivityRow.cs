namespace METERP.Application.Models;

public sealed class UserActivityRow
{
    public string UserEmail { get; init; } = string.Empty;

    public int TotalActions { get; init; }

    public int ApprovalActions { get; init; }

    public DateTime? LastActivityUtc { get; init; }
}