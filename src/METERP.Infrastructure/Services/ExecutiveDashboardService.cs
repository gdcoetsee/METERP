using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

public sealed class ExecutiveDashboardService : IExecutiveDashboardService
{
    private readonly IQuoteService _quotes;
    private readonly IStockRequisitionService _requisitions;
    private readonly ILeaveService _leave;
    private readonly IFieldReportService _fieldReports;
    private readonly ITenantNotificationService _notifications;
    private readonly IJobService _jobs;
    private readonly IInvoiceService _invoices;
    private readonly IInventoryService _inventory;

    public ExecutiveDashboardService(
        IQuoteService quotes,
        IStockRequisitionService requisitions,
        ILeaveService leave,
        IFieldReportService fieldReports,
        ITenantNotificationService notifications,
        IJobService jobs,
        IInvoiceService invoices,
        IInventoryService inventory)
    {
        _quotes = quotes;
        _requisitions = requisitions;
        _leave = leave;
        _fieldReports = fieldReports;
        _notifications = notifications;
        _jobs = jobs;
        _invoices = invoices;
        _inventory = inventory;
    }

    public async Task<ExecutiveDashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var pendingQuotes = (await _quotes.GetPendingExecutiveApprovalAsync(ct)).Count;
        var pendingReqs = (await _requisitions.GetPendingApprovalsAsync(ct)).Count;
        var pendingLeave = (await _leave.GetPendingApprovalsAsync(ct)).Count;
        var pendingField = (await _fieldReports.GetPendingAsync(ct)).Count;

        var jobs = await _jobs.GetAllAsync(pageSize: 200, ct: ct);
        var ready = jobs.Where(j => j.IsReadyToInvoice()).ToList();

        var aged = await _invoices.GetAgedDebtorsAsync(ct);
        var lowStock = (await _inventory.GetAllItemsAsync(lowStockOnly: true, ct: ct)).Count;

        return new ExecutiveDashboardSummary
        {
            PendingQuotes = pendingQuotes,
            PendingRequisitions = pendingReqs,
            PendingLeave = pendingLeave,
            PendingFieldReports = pendingField,
            PendingApprovals = pendingQuotes + pendingReqs + pendingLeave + pendingField,
            UnreadNotifications = await _notifications.GetUnreadCountAsync(ct),
            ReadyToInvoiceJobs = ready.Count,
            ReadyToInvoiceValue = ready.Sum(j => j.QuotedTotal),
            AgedDebtorsTotal = aged.Sum(a => a.BalanceDue),
            LowStockItems = lowStock
        };
    }
}