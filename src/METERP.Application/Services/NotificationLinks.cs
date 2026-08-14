namespace METERP.Application.Services;

/// <summary>
/// Maps in-app notification entity links to Blazor routes.
/// </summary>
public static class NotificationLinks
{
    public static string? ForEntity(string? relatedEntityType, Guid? relatedEntityId)
    {
        if (relatedEntityId is not { } id || id == Guid.Empty)
            return null;
        if (string.IsNullOrWhiteSpace(relatedEntityType))
            return null;

        return relatedEntityType.Trim() switch
        {
            nameof(Domain.Invoice) => $"/invoices?open={id:D}",
            nameof(Domain.Job) => $"/jobs/{id:D}",
            nameof(Domain.Quote) => $"/quotes?open={id:D}",
            nameof(Domain.StockRequisition) => $"/approvals",
            nameof(Domain.FieldReport) => $"/approvals",
            nameof(Domain.LeaveRequest) => $"/leave-admin",
            _ => null
        };
    }
}
