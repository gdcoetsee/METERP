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
    private readonly IOpportunityService _opportunities;
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IPpeIssueService _ppe;
    private readonly IEmployeeCertificationService _certs;

    public ExecutiveDashboardService(
        IQuoteService quotes,
        IStockRequisitionService requisitions,
        ILeaveService leave,
        IFieldReportService fieldReports,
        ITenantNotificationService notifications,
        IJobService jobs,
        IInvoiceService invoices,
        IInventoryService inventory,
        ISalesOrderService salesOrders,
        IOpportunityService opportunities,
        IPurchaseOrderService purchaseOrders,
        IPpeIssueService ppe,
        IEmployeeCertificationService certs)
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
        _opportunities = opportunities;
        _purchaseOrders = purchaseOrders;
        _ppe = ppe;
        _certs = certs;
    }

    public async Task<ExecutiveDashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var pendingQuoteList = await _quotes.GetPendingExecutiveApprovalAsync(ct);
        var pendingReqList = await _requisitions.GetPendingApprovalsAsync(ct);
        var pendingLeaveList = await _leave.GetPendingApprovalsAsync(ct);
        var pendingFieldList = await _fieldReports.GetPendingAsync(ct);
        var pendingQuotes = pendingQuoteList.Count;
        var pendingReqs = pendingReqList.Count;
        var pendingLeave = pendingLeaveList.Count;
        var pendingField = pendingFieldList.Count;

        var ready = await _jobs.GetReadyToInvoiceQueueAsync(20, ct);
        var deposits = await _jobs.GetDepositDueQueueAsync(20, ct);
        var awaitingSignOff = await _jobs.GetAwaitingSignOffQueueAsync(20, ct);
        var convertQuotes = await _quotes.GetUnconvertedWonQuotesAsync(10, ct);
        var convertOrders = await _salesOrders.GetUnconvertedConfirmedAsync(10, ct);
        var convertOpps = await _opportunities.GetUnquotedWonAsync(10, ct);
        var overduePos = await _purchaseOrders.GetOverdueQueueAsync(10, ct);
        var unsentPos = await _purchaseOrders.GetUnsentQueueAsync(10, ct);
        var unsentQuotes = await _quotes.GetApprovedUnsentQueueAsync(10, ct);
        var unsentInvoices = await _invoices.GetUnsentQueueAsync(10, ct);
        var unconfirmedOrders = await _salesOrders.GetUnconfirmedQueueAsync(10, ct);
        var outstandingPpe = await _ppe.GetOutstandingQueueAsync(10, ct);
        var expiringCerts = await _certs.GetExpiringQueueAsync(10, ct);

        var aged = await _invoices.GetAgedDebtorsAsync(ct);
        var overdueInvoices = aged.Where(a => a.DaysOverdue > 0).Take(8).ToList();
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
            ConvertToJobQueue = convertQuotes.Concat(convertOrders).Concat(convertOpps)
                .OrderByDescending(r => r.Total)
                .Take(12)
                .ToList(),
            AwaitingSignOffJobs = awaitingSignOff.Count,
            AwaitingSignOffValue = awaitingSignOff.Sum(j => j.UnbilledResidual),
            AwaitingSignOffQueue = awaitingSignOff,
            OverduePurchaseOrders = overduePos.Count,
            OverduePurchaseOrderValue = overduePos.Sum(p => p.Total),
            OverduePurchaseOrderQueue = overduePos,
            UnsentPurchaseOrderQueue = unsentPos,
            UnsentQuoteQueue = unsentQuotes,
            UnsentInvoiceQueue = unsentInvoices,
            UnconfirmedSalesOrderQueue = unconfirmedOrders,
            OutstandingPpeQueue = outstandingPpe,
            ExpiringCertificationQueue = expiringCerts,
            ApprovalQueue = BuildApprovalQueue(pendingQuoteList, pendingReqList, pendingLeaveList, pendingFieldList),
            OverdueInvoiceQueue = overdueInvoices
        };
    }

    private static IReadOnlyList<ApprovalQueueRow> BuildApprovalQueue(
        IReadOnlyList<Quote> quotes,
        IReadOnlyList<StockRequisition> requisitions,
        IReadOnlyList<LeaveRequest> leave,
        IReadOnlyList<FieldReport> fieldReports)
    {
        var rows = new List<ApprovalQueueRow>(quotes.Count + requisitions.Count + leave.Count + fieldReports.Count);

        foreach (var q in quotes)
        {
            rows.Add(new ApprovalQueueRow(
                q.Id,
                "Quote",
                q.QuoteNumber,
                q.Customer?.Name ?? "Customer",
                $"/approvals?tab=quotes",
                q.SubmittedForApprovalAt,
                q.ApprovalStatus.ToString()));
        }

        foreach (var r in requisitions)
        {
            rows.Add(new ApprovalQueueRow(
                r.Id,
                "REQ",
                r.RequisitionNumber,
                r.Job?.JobNumber ?? "Job",
                "/approvals?tab=requisitions",
                r.CreatedDate,
                r.Status.ToString()));
        }

        foreach (var l in leave)
        {
            var name = l.Employee != null
                ? $"{l.Employee.FirstName} {l.Employee.LastName}".Trim()
                : "Employee";
            rows.Add(new ApprovalQueueRow(
                l.Id,
                "Leave",
                name,
                $"{l.StartDate:yyyy-MM-dd}–{l.EndDate:yyyy-MM-dd}",
                "/approvals?tab=leave",
                l.CreatedDate,
                l.Status.ToString()));
        }

        foreach (var f in fieldReports)
        {
            rows.Add(new ApprovalQueueRow(
                f.Id,
                "Field",
                f.Job?.JobNumber ?? "Job",
                $"{f.HoursWorked:N1}h",
                "/approvals?tab=field",
                f.SubmittedAt == default ? null : f.SubmittedAt,
                f.Status.ToString()));
        }

        return rows
            .OrderBy(r => r.WaitingSince ?? DateTime.MaxValue)
            .Take(10)
            .ToList();
    }
}