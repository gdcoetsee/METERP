namespace METERP.Application.Services;

public interface IScheduledReportService
{
    /// <summary>
    /// Sends executive accountability summary email (no-op when SMTP not configured).
    /// </summary>
    Task<bool> SendExecutiveSummaryEmailAsync(string? toEmail = null, CancellationToken ct = default);

    /// <summary>
    /// Sends executive summaries to all active tenants with a notification email configured.
    /// Returns count of emails sent.
    /// </summary>
    Task<int> SendScheduledExecutiveReportsAsync(CancellationToken ct = default);
}