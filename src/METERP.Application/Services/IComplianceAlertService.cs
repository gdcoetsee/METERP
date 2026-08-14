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

    /// <summary>
    /// Notifies executives once per quote/requisition that has breached the tenant approval SLA.
    /// </summary>
    Task<int> RunApprovalSlaScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Notifies once per sent/accepted quote that expired without conversion to a job.
    /// </summary>
    Task<int> RunExpiredQuoteScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Notifies once per sent or partially received PO whose expected date has passed.
    /// </summary>
    Task<int> RunOverduePurchaseOrderScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Notifies once per signed-off job that has been unbilled for at least two days.
    /// </summary>
    Task<int> RunStuckReadyToInvoiceScanAsync(CancellationToken ct = default);
}