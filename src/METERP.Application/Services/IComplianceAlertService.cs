namespace METERP.Application.Services;

public interface IComplianceAlertService
{
    /// <summary>
    /// Scans company docs and employee certs; creates tenant notifications for HR + Executive at 30/14/7 day thresholds.
    /// </summary>
    Task<int> RunExpiryScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Notifies Admin/Executive once per overdue invoice with an outstanding balance.
    /// </summary>
    Task<int> RunOverdueInvoiceScanAsync(CancellationToken ct = default);
}