using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class OperationalReportService : IOperationalReportService
{
    private static readonly ReportDefinition[] Catalog =
    [
        new("jobs", "Jobs", "Job performance", "Quoted vs actual, travel, labor, margin, dates.", true, "reports-lib-jobs"),
        new("jobs-wip", "Jobs", "Work in progress", "Jobs currently in progress.", true, "reports-lib-jobs-wip"),
        new("jobs-scheduled", "Jobs", "Scheduled jobs", "Work that is booked but not started.", true, "reports-lib-jobs-scheduled"),
        new("jobs-on-hold", "Jobs", "Jobs on hold", "Paused jobs that still need attention.", true, "reports-lib-jobs-on-hold"),
        new("jobs-completed", "Jobs", "Completed jobs", "Field complete — including awaiting invoice.", true, "reports-lib-jobs-completed"),
        new("jobs-awaiting-invoice", "Jobs", "Completed awaiting invoice", "Completed or signed-off jobs with no invoice yet.", true, "reports-lib-jobs-awaiting-invoice"),
        new("jobs-ready-invoice", "Jobs", "Ready to invoice", "Dual sign-off complete, job still open.", true, "reports-lib-jobs-ready-invoice"),
        new("jobs-closed", "Jobs", "Closed jobs", "Locked jobs after billing.", true, "reports-lib-jobs-closed"),
        new("jobs-emergency", "Jobs", "Emergency / call-out", "Jobs raised without a quote.", true, "reports-lib-jobs-emergency"),
        new("jobs-variance", "Jobs", "Jobs over budget", "Actual cost above quoted.", true, "reports-lib-jobs-variance"),
        new("jobs-travel", "Jobs", "Travel by job", "Posted travel costs per job.", true, "reports-lib-jobs-travel"),
        new("division-performance", "Jobs", "Division performance", "Quoted, actual, margin by division.", true, "reports-lib-division"),
        new("quotes", "Sales", "All quotes", "Full quote book.", false, "reports-lib-quotes"),
        new("quotes-draft", "Sales", "Draft quotes", "Not yet sent to the customer.", false, "reports-lib-quotes-draft"),
        new("quotes-pending-approval", "Sales", "Quotes awaiting executive", "Submitted for executive approval.", false, "reports-lib-quotes-pending"),
        new("quotes-approved-unsent", "Sales", "Approved, not sent", "Executive approved — still to send.", false, "reports-lib-quotes-unsent"),
        new("quotes-awaiting-order", "Sales", "Quotes awaiting order", "Sent or accepted, no job yet.", false, "reports-lib-quotes-awaiting"),
        new("quotes-accepted", "Sales", "Accepted quotes", "Won quotes.", false, "reports-lib-quotes-accepted"),
        new("sales-orders", "Sales", "Open sales orders", "Confirmed or in progress.", false, "reports-lib-so"),
        new("crm", "Sales", "Opportunity pipeline", "CRM deals by stage.", false, "reports-lib-crm"),
        new("crm-weighted", "Sales", "Weighted pipeline", "Deal value × win probability.", false, "reports-lib-crm-weighted"),
        new("crm-followups", "Sales", "Follow-ups due", "Overdue and upcoming CRM follow-ups.", false, "reports-lib-crm-followups"),
        new("crm-winloss", "Sales", "Won vs lost", "Closed deals this year.", false, "reports-lib-crm-winloss"),
        new("crm-owners", "Sales", "Pipeline by owner", "Open deals grouped by owner.", false, "reports-lib-crm-owners"),
        new("invoices", "Finance", "Outstanding invoices", "Not paid or cancelled.", false, "reports-lib-invoices"),
        new("invoices-overdue", "Finance", "Overdue invoices", "Past due date and unpaid.", false, "reports-lib-invoices-overdue"),
        new("invoices-paid", "Finance", "Paid invoices", "Collected invoices.", false, "reports-lib-invoices-paid"),
        new("cashflow", "Finance", "Cashflow snapshot", "Receivables, pipeline, open POs.", false, "reports-lib-cashflow"),
        new("stock", "Supply", "Low stock", "At or below reorder level.", false, "reports-lib-stock"),
        new("requisitions-pending", "Supply", "Stock awaiting approval", "Pending manager or executive.", false, "reports-lib-req"),
        new("pos", "Supply", "Open purchase orders", "Not received or cancelled.", false, "reports-lib-pos"),
        new("ppe-outstanding", "Supply", "Outstanding PPE", "Issued PPE not yet returned.", false, "reports-lib-ppe"),
        new("workforce", "People", "Technician utilization", "Hours vs 160h monthly capacity.", false, "reports-lib-workforce"),
        new("leave-pending", "People", "Leave awaiting approval", "Manager, executive, or HR queue.", false, "reports-lib-leave"),
        new("assets", "Assets", "Asset register", "Customer plant and status.", false, "reports-lib-assets")
    ];

    private readonly AppDbContext _db;
    private readonly IWorkforceReportService _workforce;
    private readonly ICashflowReportService _cashflow;

    public OperationalReportService(
        AppDbContext db,
        IWorkforceReportService workforce,
        ICashflowReportService cashflow)
    {
        _db = db;
        _workforce = workforce;
        _cashflow = cashflow;
    }

    public IReadOnlyList<ReportDefinition> GetCatalog() => Catalog;

    public async Task<ReportTable> RunAsync(string key, Guid? divisionId = null, CancellationToken ct = default)
    {
        var def = Catalog.FirstOrDefault(c => c.Key == key)
            ?? throw new InvalidOperationException("Unknown report.");
        var div = def.SupportsDivision ? divisionId : null;

        return key switch
        {
            "jobs" => await JobPerformanceAsync(div, null, ct),
            "jobs-wip" => await JobPerformanceAsync(div, JobStatus.InProgress, ct),
            "jobs-scheduled" => await JobPerformanceAsync(div, JobStatus.Scheduled, ct),
            "jobs-on-hold" => await JobPerformanceAsync(div, JobStatus.OnHold, ct),
            "jobs-completed" => await JobPerformanceAsync(div, JobStatus.Completed, ct),
            "jobs-closed" => await JobPerformanceAsync(div, JobStatus.Closed, ct),
            "jobs-emergency" => await JobPerformanceAsync(div, null, ct, emergencyOnly: true),
            "jobs-variance" => await JobsVarianceAsync(div, ct),
            "jobs-travel" => await JobsTravelAsync(div, ct),
            "jobs-awaiting-invoice" => await JobsAwaitingInvoiceAsync(div, ct),
            "jobs-ready-invoice" => await JobsReadyToInvoiceAsync(div, ct),
            "division-performance" => await DivisionPerformanceAsync(div, ct),
            "quotes" => await QuotesAsync(null, null, converted: null, ct),
            "quotes-draft" => await QuotesAsync(QuoteStatus.Draft, null, null, ct),
            "quotes-pending-approval" => await QuotesAsync(null, QuoteApprovalStatus.PendingExecutive, null, ct),
            "quotes-approved-unsent" => await QuotesAsync(QuoteStatus.Draft, QuoteApprovalStatus.ExecutiveApproved, null, ct),
            "quotes-awaiting-order" => await QuotesAsync(null, null, converted: false, ct, sentOrAccepted: true),
            "quotes-accepted" => await QuotesAsync(QuoteStatus.Accepted, null, null, ct),
            "sales-orders" => await SalesOrdersAsync(ct),
            "crm" => await CrmAsync(ct),
            "crm-weighted" => await CrmWeightedAsync(ct),
            "crm-followups" => await CrmFollowUpsAsync(ct),
            "crm-winloss" => await CrmWinLossAsync(ct),
            "crm-owners" => await CrmOwnersAsync(ct),
            "invoices" => await InvoicesAsync(outstandingOnly: true, overdueOnly: false, paidOnly: false, ct),
            "invoices-overdue" => await InvoicesAsync(true, true, false, ct),
            "invoices-paid" => await InvoicesAsync(false, false, true, ct),
            "cashflow" => await CashflowAsync(ct),
            "stock" => await StockAsync(ct),
            "requisitions-pending" => await RequisitionsAsync(ct),
            "pos" => await PurchaseOrdersAsync(ct),
            "ppe-outstanding" => await PpeOutstandingAsync(ct),
            "workforce" => await WorkforceAsync(ct),
            "leave-pending" => await LeaveAsync(ct),
            "assets" => await AssetsAsync(ct),
            _ => throw new InvalidOperationException("Unknown report.")
        };
    }

    public async Task<JobPerformanceDetail?> GetJobPerformanceAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await JobsQuery()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null) return null;
        return ToDetail(job);
    }

    private IQueryable<Job> JobsQuery() =>
        _db.Set<Job>()
            .AsNoTracking()
            .Include(j => j.Customer)
            .Include(j => j.Division)
            .Include(j => j.AssignedEmployee)
            .Include(j => j.ActualCosts)
            .Include(j => j.Labors);

    private async Task<ReportTable> JobPerformanceAsync(
        Guid? divisionId,
        JobStatus? status,
        CancellationToken ct,
        bool emergencyOnly = false)
    {
        var q = JobsQuery();
        if (divisionId is { } d && d != Guid.Empty)
            q = q.Where(j => j.DivisionId == d);
        if (status.HasValue)
            q = q.Where(j => j.Status == status.Value);
        if (emergencyOnly)
            q = q.Where(j => j.IsEmergency);

        var jobs = await q.OrderByDescending(j => j.CreatedDate).Take(500).ToListAsync(ct);
        var headers = new[]
        {
            "Job", "Customer", "Division", "Status", "Quoted", "Labor", "Travel", "Materials", "Other",
            "Actual", "Variance", "Margin %", "Scheduled", "Completed", "Assigned"
        };
        var rows = jobs.Select(ToJobRow).ToList();
        var quoted = jobs.Sum(j => j.QuotedTotal);
        var actual = jobs.Sum(j => j.GetActualTotal());
        return new ReportTable(
            status.HasValue ? $"Jobs — {status}" : (emergencyOnly ? "Emergency jobs" : "Job performance"),
            headers,
            rows,
            $"{jobs.Count} job(s). Quoted R {quoted:N0} · actual R {actual:N0} · variance R {(actual - quoted):N0}.",
            jobs.Select(j => j.Id).ToList());
    }

    private async Task<ReportTable> JobsAwaitingInvoiceAsync(Guid? divisionId, CancellationToken ct)
    {
        var invoiced = await _db.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.JobId != null && i.Status != InvoiceStatus.Cancelled)
            .Select(i => i.JobId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var q = JobsQuery().Where(j =>
            j.Status == JobStatus.Completed
            || j.Status == JobStatus.Invoiced
            || j.SignOffStatus == JobSignOffStatus.SignedOff);
        q = q.Where(j => j.Status != JobStatus.Closed && j.Status != JobStatus.Cancelled);
        if (divisionId is { } d && d != Guid.Empty)
            q = q.Where(j => j.DivisionId == d);

        var jobs = (await q.Take(500).ToListAsync(ct))
            .Where(j => !invoiced.Contains(j.Id))
            .ToList();

        return new ReportTable(
            "Completed awaiting invoice",
            JobHeaders,
            jobs.Select(ToJobRow).ToList(),
            $"{jobs.Count} job(s) finished in the field with no invoice on file.",
            jobs.Select(j => j.Id).ToList());
    }

    private async Task<ReportTable> JobsReadyToInvoiceAsync(Guid? divisionId, CancellationToken ct)
    {
        var q = JobsQuery().Where(j =>
            j.SignOffStatus == JobSignOffStatus.SignedOff
            && j.Status != JobStatus.Closed
            && j.Status != JobStatus.Cancelled);
        if (divisionId is { } d && d != Guid.Empty)
            q = q.Where(j => j.DivisionId == d);
        var jobs = await q.Take(500).ToListAsync(ct);
        return new ReportTable("Ready to invoice", JobHeaders, jobs.Select(ToJobRow).ToList(),
            $"{jobs.Count} signed-off job(s) still open for billing.",
            jobs.Select(j => j.Id).ToList());
    }

    private async Task<ReportTable> DivisionPerformanceAsync(Guid? divisionId, CancellationToken ct)
    {
        var q = JobsQuery();
        if (divisionId is { } d && d != Guid.Empty)
            q = q.Where(j => j.DivisionId == d);
        var jobs = await q.Take(1000).ToListAsync(ct);
        var groups = jobs
            .GroupBy(j => j.Division?.Name ?? "Unassigned")
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var quoted = g.Sum(j => j.QuotedTotal);
                var actual = g.Sum(j => j.GetActualTotal());
                var margin = quoted <= 0 ? 0m : Math.Round((quoted - actual) / quoted * 100m, 1);
                return new[]
                {
                    g.Key,
                    g.Count().ToString(),
                    g.Count(j => j.Status == JobStatus.InProgress).ToString(),
                    g.Count(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Closed).ToString(),
                    Money(quoted),
                    Money(actual),
                    Money(actual - quoted),
                    $"{margin:0.0}%"
                };
            })
            .ToList();
        return new ReportTable(
            "Division performance",
            ["Division", "Jobs", "WIP", "Completed/closed", "Quoted", "Actual", "Variance", "Margin %"],
            groups,
            $"{groups.Count} division(s).");
    }

    private async Task<ReportTable> QuotesAsync(
        QuoteStatus? status,
        QuoteApprovalStatus? approval,
        bool? converted,
        CancellationToken ct,
        bool sentOrAccepted = false)
    {
        var convertedIds = await _db.Set<Job>()
            .AsNoTracking()
            .Where(j => j.QuoteId != null)
            .Select(j => j.QuoteId!.Value)
            .ToListAsync(ct);

        var q = _db.Set<Quote>().AsNoTracking().Include(x => x.Customer).AsQueryable();
        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);
        if (approval.HasValue)
            q = q.Where(x => x.ApprovalStatus == approval.Value);
        if (sentOrAccepted)
            q = q.Where(x => x.Status == QuoteStatus.Sent || x.Status == QuoteStatus.Accepted);
        var list = await q.OrderByDescending(x => x.QuoteDate).Take(500).ToListAsync(ct);
        if (converted == false)
            list = list.Where(x => !convertedIds.Contains(x.Id)).ToList();
        else if (converted == true)
            list = list.Where(x => convertedIds.Contains(x.Id)).ToList();

        var rows = list.Select(x => new[]
        {
            x.QuoteNumber,
            x.Customer?.Name ?? "—",
            x.Status.ToString(),
            x.ApprovalStatus.ToString(),
            Money(x.Total),
            Date(x.QuoteDate),
            Date(x.ValidUntil),
            convertedIds.Contains(x.Id) ? "Yes" : "No"
        }).ToList();
        return new ReportTable("Quotes", ["Number", "Customer", "Status", "Approval", "Total", "Date", "Valid until", "Converted"], rows,
            $"{list.Count} quote(s) · R {list.Sum(x => x.Total):N0}.");
    }

    private async Task<ReportTable> SalesOrdersAsync(CancellationToken ct)
    {
        var list = await _db.Set<SalesOrder>().AsNoTracking().Include(s => s.Customer)
            .Where(s => s.Status == SalesOrderStatus.Confirmed || s.Status == SalesOrderStatus.InProgress)
            .OrderByDescending(s => s.CreatedDate)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(s => new[]
        {
            s.SoNumber,
            s.Customer?.Name ?? "—",
            s.Status.ToString(),
            Money(s.Total),
            Date(s.CreatedDate)
        }).ToList();
        return new ReportTable("Open sales orders", ["SO", "Customer", "Status", "Total", "Created"], rows, $"{list.Count} open SO(s).");
    }

    private async Task<ReportTable> CrmAsync(CancellationToken ct)
    {
        var list = await LoadDealsAsync(ct);
        var rows = list.Select(o => new[]
        {
            o.Title,
            o.Customer?.Name ?? o.CustomerName ?? "—",
            o.Stage.ToString(),
            OwnerLabel(o),
            o.Priority.ToString(),
            $"{o.ProbabilityPercent}%",
            Money(o.Value),
            Money(o.WeightedValue),
            Date(o.ExpectedClose),
            Date(o.NextFollowUp),
            o.QuoteId.HasValue && o.QuoteId != Guid.Empty ? "Yes" : "No"
        }).ToList();
        return new ReportTable("Opportunity pipeline",
            ["Deal", "Company", "Stage", "Owner", "Priority", "Prob", "Value", "Weighted", "Close", "Follow-up", "Has quote"],
            rows,
            $"{list.Count} deal(s) · R {list.Sum(o => o.Value):N0} · weighted R {list.Sum(o => o.WeightedValue):N0}.");
    }

    private async Task<ReportTable> CrmWeightedAsync(CancellationToken ct)
    {
        var list = await LoadDealsAsync(ct);
        var open = list.Where(o => o.Stage is not OpportunityStage.ClosedWon and not OpportunityStage.ClosedLost).ToList();
        var groups = open.GroupBy(o => o.Stage).OrderBy(g => g.Key);
        var rows = groups.Select(g => new[]
        {
            g.Key.ToString(),
            g.Count().ToString(),
            Money(g.Sum(o => o.Value)),
            Money(g.Sum(o => o.WeightedValue))
        }).ToList();
        return new ReportTable("Weighted pipeline",
            ["Stage", "Deals", "Value", "Weighted"],
            rows,
            $"Open R {open.Sum(o => o.Value):N0} · weighted R {open.Sum(o => o.WeightedValue):N0}.");
    }

    private async Task<ReportTable> CrmFollowUpsAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var list = (await LoadDealsAsync(ct))
            .Where(o => o.Stage is not OpportunityStage.ClosedWon and not OpportunityStage.ClosedLost)
            .Where(o => o.NextFollowUp.HasValue)
            .OrderBy(o => o.NextFollowUp)
            .ToList();
        var rows = list.Select(o => new[]
        {
            o.Title,
            o.Customer?.Name ?? o.CustomerName ?? "—",
            OwnerLabel(o),
            Date(o.NextFollowUp),
            o.NextFollowUp!.Value.Date < today ? "Overdue" : "Upcoming",
            Money(o.Value)
        }).ToList();
        var overdue = list.Count(o => o.NextFollowUp!.Value.Date < today);
        return new ReportTable("Follow-ups due",
            ["Deal", "Company", "Owner", "Follow-up", "Status", "Value"],
            rows,
            $"{overdue} overdue · {list.Count - overdue} upcoming.");
    }

    private async Task<ReportTable> CrmWinLossAsync(CancellationToken ct)
    {
        var yearStart = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = (await LoadDealsAsync(ct))
            .Where(o => o.Stage is OpportunityStage.ClosedWon or OpportunityStage.ClosedLost)
            .Where(o => (o.LastActivityAt ?? o.LastModifiedDate ?? o.CreatedDate) >= yearStart)
            .ToList();
        var won = list.Where(o => o.Stage == OpportunityStage.ClosedWon).ToList();
        var lost = list.Where(o => o.Stage == OpportunityStage.ClosedLost).ToList();
        var rows = list
            .OrderByDescending(o => o.LastActivityAt ?? o.CreatedDate)
            .Select(o => new[]
            {
                o.Title,
                o.Customer?.Name ?? o.CustomerName ?? "—",
                o.Stage == OpportunityStage.ClosedWon ? "Won" : "Lost",
                Money(o.Value),
                o.LossReason ?? "—",
                Date(o.LastActivityAt ?? o.CreatedDate)
            }).ToList();
        var winRate = list.Count == 0 ? 0 : Math.Round(100m * won.Count / list.Count, 1);
        return new ReportTable("Won vs lost",
            ["Deal", "Company", "Result", "Value", "Loss reason", "Closed"],
            rows,
            $"{won.Count} won R {won.Sum(o => o.Value):N0} · {lost.Count} lost R {lost.Sum(o => o.Value):N0} · win rate {winRate}%.");
    }

    private async Task<ReportTable> CrmOwnersAsync(CancellationToken ct)
    {
        var open = (await LoadDealsAsync(ct))
            .Where(o => o.Stage is not OpportunityStage.ClosedWon and not OpportunityStage.ClosedLost)
            .ToList();
        var rows = open
            .GroupBy(o => OwnerLabel(o))
            .OrderByDescending(g => g.Sum(o => o.WeightedValue))
            .Select(g => new[]
            {
                g.Key,
                g.Count().ToString(),
                Money(g.Sum(o => o.Value)),
                Money(g.Sum(o => o.WeightedValue)),
                g.Count(o => o.NextFollowUp.HasValue && o.NextFollowUp.Value.Date < DateTime.UtcNow.Date).ToString()
            }).ToList();
        return new ReportTable("Pipeline by owner",
            ["Owner", "Deals", "Value", "Weighted", "Overdue follow-ups"],
            rows,
            $"{open.Count} open deal(s).");
    }

    private async Task<List<Opportunity>> LoadDealsAsync(CancellationToken ct) =>
        await _db.Set<Opportunity>().AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.OwnerEmployee)
            .OrderBy(o => o.Stage).ThenBy(o => o.BoardOrder)
            .Take(500)
            .ToListAsync(ct);

    private static string OwnerLabel(Opportunity o) =>
        o.OwnerEmployee == null
            ? "Unassigned"
            : $"{o.OwnerEmployee.FirstName} {o.OwnerEmployee.LastName}".Trim();

    private async Task<ReportTable> JobsVarianceAsync(Guid? divisionId, CancellationToken ct)
    {
        var jobs = await JobsQuery()
            .Where(j => !divisionId.HasValue || j.DivisionId == divisionId)
            .Take(500)
            .ToListAsync(ct);
        var over = jobs.Where(j =>
        {
            var (_, _, _, _, actual) = CostSplit(j);
            return actual > j.QuotedTotal && j.QuotedTotal > 0;
        }).ToList();
        var rows = over.Select(ToJobRow).ToList();
        return new ReportTable("Jobs over budget", JobHeaders, rows, $"{over.Count} job(s) over quoted.",
            over.Select(j => j.Id).ToList());
    }

    private async Task<ReportTable> JobsTravelAsync(Guid? divisionId, CancellationToken ct)
    {
        var jobs = await JobsQuery()
            .Where(j => !divisionId.HasValue || j.DivisionId == divisionId)
            .Take(500)
            .ToListAsync(ct);
        var withTravel = jobs.Select(j =>
        {
            var (_, travel, _, _, _) = CostSplit(j);
            return (Job: j, Travel: travel);
        }).Where(x => x.Travel > 0).OrderByDescending(x => x.Travel).ToList();
        var rows = withTravel.Select(x => new[]
        {
            x.Job.JobNumber,
            x.Job.Customer?.Name ?? "—",
            x.Job.Status.ToString(),
            Money(x.Job.QuotedTotal),
            Money(x.Travel),
            Date(x.Job.ScheduledStart)
        }).ToList();
        return new ReportTable("Travel by job",
            ["Job", "Customer", "Status", "Quoted", "Travel", "Scheduled"],
            rows,
            $"R {withTravel.Sum(x => x.Travel):N2} travel across {withTravel.Count} job(s).",
            withTravel.Select(x => x.Job.Id).ToList());
    }

    private async Task<ReportTable> PpeOutstandingAsync(CancellationToken ct)
    {
        var list = await _db.Set<EmployeePpeIssue>().AsNoTracking()
            .Include(p => p.Employee)
            .Include(p => p.InventoryItem)
            .Include(p => p.Job)
            .Where(p => p.Quantity > p.QuantityReturned)
            .OrderBy(p => p.IssuedAt)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(p => new[]
        {
            p.Employee == null ? "—" : $"{p.Employee.FirstName} {p.Employee.LastName}".Trim(),
            p.InventoryItem?.Sku ?? "—",
            p.InventoryItem?.Name ?? "—",
            p.QuantityOutstanding.ToString("N1"),
            p.Job?.JobNumber ?? "—",
            Date(p.IssuedAt)
        }).ToList();
        return new ReportTable("Outstanding PPE",
            ["Employee", "SKU", "Item", "Outstanding", "Job", "Issued"],
            rows,
            $"{list.Count} issue(s) still out.");
    }

    private async Task<ReportTable> InvoicesAsync(bool outstandingOnly, bool overdueOnly, bool paidOnly, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var q = _db.Set<Invoice>().AsNoTracking().Include(i => i.Customer).Include(i => i.Job).AsQueryable();
        if (paidOnly)
            q = q.Where(i => i.Status == InvoiceStatus.Paid);
        else if (outstandingOnly)
            q = q.Where(i => i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled);
        var list = await q.OrderByDescending(i => i.InvoiceDate).Take(500).ToListAsync(ct);
        if (overdueOnly)
            list = list.Where(i => i.DueDate.Date < today && i.Status != InvoiceStatus.Paid && i.Status != InvoiceStatus.Cancelled).ToList();

        var rows = list.Select(i => new[]
        {
            i.InvoiceNumber,
            i.Customer?.Name ?? "—",
            i.Job?.JobNumber ?? "—",
            i.Status.ToString(),
            Money(i.Total),
            Money(i.BalanceDue),
            Date(i.InvoiceDate),
            Date(i.DueDate)
        }).ToList();
        return new ReportTable("Invoices", ["Number", "Customer", "Job", "Status", "Total", "Balance", "Date", "Due"], rows,
            $"{list.Count} invoice(s) · balance R {list.Sum(i => i.BalanceDue):N0}.");
    }

    private async Task<ReportTable> CashflowAsync(CancellationToken ct)
    {
        var snap = await _cashflow.GetCashflowForecastAsync(ct);
        var rows = new List<string[]>
        {
            new[] { "Receivable inflow", Money(snap.ReceivableInflow), snap.ReceivableInvoiceCount.ToString() },
            new[] { "Pipeline (accepted quotes)", Money(snap.PipelineInflow), snap.PipelineQuoteCount.ToString() },
            new[] { "Open PO outflow", Money(snap.CommittedOutflow), snap.OpenPurchaseOrderCount.ToString() },
            new[] { "Net forecast", Money(snap.NetForecastInflow), "" }
        };
        return new ReportTable("Cashflow snapshot", ["Item", "Amount", "Count"], rows);
    }

    private async Task<ReportTable> StockAsync(CancellationToken ct)
    {
        var items = await _db.Set<InventoryItem>().AsNoTracking()
            .Where(i => i.QuantityOnHand <= i.ReorderLevel)
            .OrderBy(i => i.Sku)
            .Take(500)
            .ToListAsync(ct);
        var rows = items.Select(i => new[]
        {
            i.Sku, i.Name, i.QuantityOnHand.ToString("N2"), i.QuantityReserved.ToString("N2"), i.ReorderLevel.ToString("N2")
        }).ToList();
        return new ReportTable("Low stock", ["SKU", "Name", "On hand", "Reserved", "Reorder"], rows, $"{items.Count} SKU(s) at or below reorder.");
    }

    private async Task<ReportTable> RequisitionsAsync(CancellationToken ct)
    {
        var list = await _db.Set<StockRequisition>().AsNoTracking().Include(r => r.Job)
            .Where(r => r.Status == RequisitionStatus.PendingManager || r.Status == RequisitionStatus.PendingExecutive)
            .OrderBy(r => r.CreatedDate)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(r => new[]
        {
            r.RequisitionNumber,
            r.Job?.JobNumber ?? "—",
            r.Status.ToString(),
            r.IsPpe ? "PPE" : "—",
            Date(r.CreatedDate)
        }).ToList();
        return new ReportTable("Stock awaiting approval", ["REQ", "Job", "Status", "PPE", "Submitted"], rows, $"{list.Count} requisition(s).");
    }

    private async Task<ReportTable> PurchaseOrdersAsync(CancellationToken ct)
    {
        var list = await _db.Set<PurchaseOrder>().AsNoTracking().Include(p => p.Supplier)
            .Where(p => p.Status != PurchaseOrderStatus.Received && p.Status != PurchaseOrderStatus.Cancelled)
            .OrderByDescending(p => p.PoDate)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(p => new[]
        {
            p.PoNumber, p.Supplier?.Name ?? "—", p.Status.ToString(), Money(p.Total), Date(p.PoDate)
        }).ToList();
        return new ReportTable("Open purchase orders", ["PO", "Supplier", "Status", "Total", "Date"], rows,
            $"{list.Count} PO(s) · R {list.Sum(p => p.Total):N0}.");
    }

    private async Task<ReportTable> WorkforceAsync(CancellationToken ct)
    {
        var rows = (await _workforce.GetTechnicianUtilizationAsync(ct: ct))
            .Select(t => new[] { t.Name, t.HoursLogged.ToString("N1"), t.CapacityHours.ToString("N0"), $"{t.UtilizationPercent:0.0}%" })
            .ToList();
        return new ReportTable("Technician utilization", ["Technician", "Hours", "Capacity", "Utilization"], rows);
    }

    private async Task<ReportTable> LeaveAsync(CancellationToken ct)
    {
        var list = await _db.Set<LeaveRequest>().AsNoTracking().Include(r => r.Employee)
            .Where(r => r.Status == LeaveRequestStatus.PendingManager
                || r.Status == LeaveRequestStatus.PendingExecutive
                || r.Status == LeaveRequestStatus.PendingHr)
            .OrderBy(r => r.StartDate)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(r => new[]
        {
            r.Employee != null ? $"{r.Employee.FirstName} {r.Employee.LastName}" : "—",
            Date(r.StartDate),
            Date(r.EndDate),
            r.DaysRequested.ToString("N1"),
            r.Status.ToString(),
            r.Reason ?? "—"
        }).ToList();
        return new ReportTable("Leave awaiting approval", ["Employee", "Start", "End", "Days", "Stage", "Reason"], rows, $"{list.Count} request(s).");
    }

    private async Task<ReportTable> AssetsAsync(CancellationToken ct)
    {
        var list = await _db.Set<Asset>().AsNoTracking().Include(a => a.Customer)
            .OrderBy(a => a.AssetNumber)
            .Take(500)
            .ToListAsync(ct);
        var rows = list.Select(a => new[]
        {
            a.AssetNumber, a.Name, a.AssetType, a.Customer?.Name ?? "—", a.Status.ToString(), a.Location ?? "—"
        }).ToList();
        return new ReportTable("Asset register", ["Number", "Name", "Type", "Customer", "Status", "Location"], rows, $"{list.Count} asset(s).");
    }

    private static readonly string[] JobHeaders =
    [
        "Job", "Customer", "Division", "Status", "Quoted", "Labor", "Travel", "Materials", "Other",
        "Actual", "Variance", "Margin %", "Scheduled", "Completed", "Assigned"
    ];

    private static string[] ToJobRow(Job j)
    {
        var (labor, travel, materials, other, actual) = CostSplit(j);
        return
        [
            j.JobNumber,
            j.Customer?.Name ?? "—",
            j.Division?.Name ?? "—",
            j.Status.ToString(),
            Money(j.QuotedTotal),
            Money(labor),
            Money(travel),
            Money(materials),
            Money(other),
            Money(actual),
            Money(actual - j.QuotedTotal),
            $"{j.GetMarginPercent():0.0}%",
            Date(j.ScheduledStart),
            Date(j.CompletedDate),
            j.AssignedEmployee != null ? $"{j.AssignedEmployee.FirstName} {j.AssignedEmployee.LastName}" : "—"
        ];
    }

    private static JobPerformanceDetail ToDetail(Job j)
    {
        var (labor, travel, materials, other, actual) = CostSplit(j);
        var costs = j.ActualCosts.Where(c => !c.IsDeleted)
            .OrderBy(c => c.CostDate)
            .Select(c => new JobPerformanceCostLine(c.CostType, c.Description, c.Amount, c.CostDate))
            .ToList();
        var laborLines = j.Labors.Where(l => !l.IsDeleted)
            .OrderBy(l => l.WorkDate)
            .Select(l => new JobPerformanceLaborLine(l.Technician ?? l.Employee?.FirstName ?? "—", l.Hours, l.HourlyRate, l.TotalCost, l.WorkDate))
            .ToList();
        return new JobPerformanceDetail(
            j.Id, j.JobNumber, j.Title, j.Customer?.Name ?? "—", j.Division?.Name ?? "—",
            j.Status.ToString(), j.SignOffStatus.ToString(),
            j.QuotedTotal, labor, travel, materials, other, actual, actual - j.QuotedTotal, j.GetMarginPercent(),
            j.ScheduledStart, j.CompletedDate, j.ClosedAt,
            j.AssignedEmployee != null ? $"{j.AssignedEmployee.FirstName} {j.AssignedEmployee.LastName}" : "—",
            costs, laborLines);
    }

    private static (decimal Labor, decimal Travel, decimal Materials, decimal Other, decimal Actual) CostSplit(Job j)
    {
        var labor = j.Labors.Where(l => !l.IsDeleted).Sum(l => l.TotalCost);
        var costs = j.ActualCosts.Where(c => !c.IsDeleted).ToList();
        var travel = costs.Where(c => string.Equals(c.CostType, "Travel", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
        var materials = costs.Where(c => string.Equals(c.CostType, "Material", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
        var other = costs.Where(c =>
                !string.Equals(c.CostType, "Travel", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(c.CostType, "Material", StringComparison.OrdinalIgnoreCase))
            .Sum(c => c.Amount);
        return (labor, travel, materials, other, labor + travel + materials + other);
    }

    private static string Money(decimal v) => v.ToString("N2");
    private static string Date(DateTime? v) => v.HasValue ? v.Value.ToString("yyyy-MM-dd") : "—";
    private static string Date(DateTime v) => v.ToString("yyyy-MM-dd");
}
