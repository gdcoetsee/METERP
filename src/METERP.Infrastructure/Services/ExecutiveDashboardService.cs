using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using Microsoft.Extensions.Logging;

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
    private readonly ICompanyDocumentService _companyDocuments;
    private readonly ILogger<ExecutiveDashboardService>? _logger;

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
        IEmployeeCertificationService certs,
        ICompanyDocumentService companyDocuments,
        ILogger<ExecutiveDashboardService>? logger = null)
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
        _companyDocuments = companyDocuments;
        _logger = logger;
    }

    /// <summary>
    /// Noted on a real queue failure. Not a skip gate: a slow earlier queue must not blank the rest of the desk.
    /// </summary>
    private static readonly TimeSpan DeskBudget = TimeSpan.FromSeconds(4);

    public async Task<ExecutiveDashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var pendingQuoteList = await LoadQueueAsync("quote approvals", () => _quotes.GetPendingExecutiveApprovalAsync(ct), (IReadOnlyList<Quote>)Array.Empty<Quote>(), warnings);
        var pendingReqList = await LoadQueueAsync("requisitions", () => _requisitions.GetPendingApprovalsAsync(ct), (IReadOnlyList<StockRequisition>)Array.Empty<StockRequisition>(), warnings);
        var pendingLeaveList = await LoadQueueAsync("leave", () => _leave.GetPendingApprovalsAsync(ct), (IReadOnlyList<LeaveRequest>)Array.Empty<LeaveRequest>(), warnings);
        var pendingFieldList = await LoadQueueAsync("field reports", () => _fieldReports.GetPendingAsync(ct), (IReadOnlyList<FieldReport>)Array.Empty<FieldReport>(), warnings);
        var pendingQuotes = pendingQuoteList.Count;
        var pendingReqs = pendingReqList.Count;
        var pendingLeave = pendingLeaveList.Count;
        var pendingField = pendingFieldList.Count;

        var ready = await LoadQueueAsync("ready to invoice", () => _jobs.GetReadyToInvoiceQueueAsync(20, ct), (IReadOnlyList<ReadyToInvoiceJobRow>)Array.Empty<ReadyToInvoiceJobRow>(), warnings);
        var deposits = await LoadQueueAsync("deposits", () => _jobs.GetDepositDueQueueAsync(20, ct), (IReadOnlyList<ReadyToInvoiceJobRow>)Array.Empty<ReadyToInvoiceJobRow>(), warnings);
        var awaitingSignOff = await LoadQueueAsync("sign-off", () => _jobs.GetAwaitingSignOffQueueAsync(20, ct), (IReadOnlyList<ReadyToInvoiceJobRow>)Array.Empty<ReadyToInvoiceJobRow>(), warnings);
        var convertQuotes = await LoadQueueAsync("won quotes", () => _quotes.GetUnconvertedWonQuotesAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var convertOrders = await LoadQueueAsync("sales orders", () => _salesOrders.GetUnconvertedConfirmedAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var convertOpps = await LoadQueueAsync("opportunities", () => _opportunities.GetUnquotedWonAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var proposalQuotes = await LoadQueueAsync("proposal quotes", () => _opportunities.GetUnquotedProposalQueueAsync(12, ct), new ProposalQuoteQueueResult(), warnings);
        var overduePos = await LoadQueueAsync("overdue purchase orders", () => _purchaseOrders.GetOverdueQueueAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var unsentPos = await LoadQueueAsync("unsent purchase orders", () => _purchaseOrders.GetUnsentQueueAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var unsentQuotes = await LoadQueueAsync("unsent quotes", () => _quotes.GetApprovedUnsentQueueAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var unsentInvoices = await LoadQueueAsync("unsent invoices", () => _invoices.GetUnsentQueueAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var unconfirmedOrders = await LoadQueueAsync("unconfirmed sales orders", () => _salesOrders.GetUnconfirmedQueueAsync(10, ct), (IReadOnlyList<ConvertibleDocumentRow>)Array.Empty<ConvertibleDocumentRow>(), warnings);
        var outstandingPpe = await LoadQueueAsync("PPE", () => _ppe.GetOutstandingQueueAsync(10, ct), (IReadOnlyList<PpeOutstandingRow>)Array.Empty<PpeOutstandingRow>(), warnings);
        var expiringCerts = await LoadQueueAsync("certifications", () => _certs.GetExpiringQueueAsync(10, ct), (IReadOnlyList<CertificationExpiryRow>)Array.Empty<CertificationExpiryRow>(), warnings);
        var expiringDocs = await LoadQueueAsync("company documents", () => _companyDocuments.GetExpiringQueueAsync(10, ct), (IReadOnlyList<CompanyDocumentExpiryRow>)Array.Empty<CompanyDocumentExpiryRow>(), warnings);

        var aged = await LoadQueueAsync("aged debtors", () => _invoices.GetAgedDebtorsAsync(ct), (IReadOnlyList<AgedDebtorRow>)Array.Empty<AgedDebtorRow>(), warnings);
        var overdueInvoices = aged.Where(a => a.DaysOverdue > 0).Take(8).ToList();
        var lowStockItems = await LoadQueueAsync(
            "low stock",
            () => _inventory.GetAllItemsAsync(lowStockOnly: true, pageSize: 10, ct: ct),
            (IReadOnlyList<InventoryItem>)Array.Empty<InventoryItem>(),
            warnings);
        var unread = await LoadQueueAsync("notifications", () => _notifications.GetUnreadCountAsync(ct), 0, warnings);

        return new ExecutiveDashboardSummary
        {
            PendingQuotes = pendingQuotes,
            PendingRequisitions = pendingReqs,
            PendingLeave = pendingLeave,
            PendingFieldReports = pendingField,
            PendingApprovals = pendingQuotes + pendingReqs + pendingLeave + pendingField,
            UnreadNotifications = unread,
            ReadyToInvoiceJobs = ready.Count,
            ReadyToInvoiceValue = ready.Sum(j => j.UnbilledResidual > 0 ? j.UnbilledResidual : j.QuotedTotal),
            AgedDebtorsTotal = aged.Sum(a => a.BalanceDue),
            LowStockItems = lowStockItems.Count,
            LowStockQueue = lowStockItems
                .Select(i => new LowStockRow(
                    i.Id,
                    i.Sku,
                    i.Name,
                    i.QuantityOnHand,
                    i.ReorderLevel,
                    "/inventory"))
                .ToList(),
            ReadyToInvoiceQueue = ready,
            DepositDueJobs = deposits.Count,
            DepositDueValue = deposits.Sum(j => j.UnbilledResidual),
            DepositDueQueue = deposits,
            ConvertToJobQueue = convertQuotes.Concat(convertOrders).Concat(convertOpps)
                .OrderByDescending(r => r.Total)
                .Take(12)
                .ToList(),
            UnquotedProposalDeals = proposalQuotes.TotalCount,
            ProposalQuoteQueue = proposalQuotes.Items,
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
            ExpiringCompanyDocumentQueue = expiringDocs,
            ApprovalQueue = BuildApprovalQueue(pendingQuoteList, pendingReqList, pendingLeaveList, pendingFieldList),
            OverdueInvoiceQueue = overdueInvoices,
            LoadWarnings = warnings
        };
    }

    private async Task<T> LoadQueueAsync<T>(string name, Func<Task<T>> load, T fallback, List<string> warnings)
    {
        try
        {
            return await load();
        }
        catch (Exception ex)
        {
            warnings.Add(name);
            _logger?.LogWarning(
                ex,
                "Cash desk queue {QueueName} failed. Desk budget is {DeskBudgetSeconds}s and later queues still load.",
                name,
                DeskBudget.TotalSeconds);
            return fallback;
        }
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
                $"{l.StartDate:yyyy-MM-dd}â€“{l.EndDate:yyyy-MM-dd}",
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