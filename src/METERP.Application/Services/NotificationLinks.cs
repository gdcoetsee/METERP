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
            nameof(Domain.Opportunity) => $"/opportunities?open={id:D}",
            nameof(Domain.PurchaseOrder) => $"/purchase-orders?open={id:D}",
            nameof(Domain.StockRequisition) => $"/approvals?tab=requisitions",
            nameof(Domain.FieldReport) => $"/approvals?tab=field",
            nameof(Domain.LeaveRequest) => $"/approvals?tab=leave",
            _ => null
        };
    }
}
