namespace METERP.Application.Services;

/// <summary>
/// Dispatches invoice lifecycle integrations: tenant webhook + optional SMTP notification.
/// </summary>
public interface IInvoiceIntegrationService
{
    Task NotifyInvoiceCreatedAsync(Guid invoiceId, CancellationToken ct = default);
}