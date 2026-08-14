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
    private readonly ISalesOrderService _salesOrders;

    public ExecutiveDashboardService(
        IQuoteService quotes,
        IStockRequisitionService requisitions,
        ILeaveService leave,
        IFieldReportService fieldReports,
        ITenantNotificationService notifications,
        IJobService jobs,
        IInvoiceService invoices,
        IInventoryService inventory,
        ISalesOrderService salesOrders)
    {
        _quotes = quotes;
        _requisitions = requisitions;
        _leave = leave;
        _fieldReports = fieldReports;
        _notifications = notifications;
        _jobs = jobs;
        _invoices = invoices;
        _inventory = inventory;
        _salesOrders = salesOrders;
    }

    public async Task<ExecutiveDashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var pendingQuotes = (await _quotes.GetPendingExecutiveApprovalAsync(ct)).Count;
        var pendingReqs = (await _requisitions.GetPendingApprovalsAsync(ct)).Count;
        var pendingLeave = (await _leave.GetPendingApprovalsAsync(ct)).Count;
        var pendingField = (await _fieldReports.GetPendingAsync(ct)).Count;

        var ready = await _jobs.GetReadyToInvoiceQueueAsync(20, ct);
        var deposits = await _jobs.GetDepositDueQueueAsync(20, ct);
        var awaitingSignOff = await _jobs.GetAwaitingSignOffQueueAsync(20, ct);
        var convertQuotes = await _quotes.GetUnconvertedWonQuotesAsync(10, ct);
        var convertOrders = await _salesOrders.GetUnconvertedConfirmedAsync(10, ct);

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
            ReadyToInvoiceValue = ready.Sum(j => j.UnbilledResidual > 0 ? j.UnbilledResidual : j.QuotedTotal),
            AgedDebtorsTotal = aged.Sum(a => a.BalanceDue),
            LowStockItems = lowStock,
            ReadyToInvoiceQueue = ready,
            DepositDueJobs = deposits.Count,
            DepositDueValue = deposits.Sum(j => j.UnbilledResidual),
            DepositDueQueue = deposits,
            ConvertToJobQueue = convertQuotes.Concat(convertOrders)
                .OrderByDescending(r => r.Total)
                .Take(12)
                .ToList(),
            AwaitingSignOffJobs = awaitingSignOff.Count,
            AwaitingSignOffValue = awaitingSignOff.Sum(j => j.UnbilledResidual),
            AwaitingSignOffQueue = awaitingSignOff
        };
    }
}