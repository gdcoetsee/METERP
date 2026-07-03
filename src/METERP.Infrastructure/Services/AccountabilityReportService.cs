using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class AccountabilityReportService : IAccountabilityReportService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantProvider _tenantProvider;

    public AccountabilityReportService(AppDbContext dbContext, ITenantProvider tenantProvider)
    {
        _dbContext = dbContext;
        _tenantProvider = tenantProvider;
    }

    public async Task<IReadOnlyList<DivisionScorecardRow>> GetDivisionScorecardsAsync(CancellationToken ct = default)
    {
        var divisions = await _dbContext.Set<Division>()
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var jobs = await _dbContext.Set<Job>()
            .AsNoTracking()
            .Include(j => j.Milestones)
            .Where(j => j.DivisionId.HasValue)
            .ToListAsync(ct);

        var invoices = await _dbContext.Set<Invoice>()
            .AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Paid || i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.PartiallyPaid)
            .Select(i => new { i.JobId, i.Total })
            .ToListAsync(ct);

        var revenueByJob = invoices
            .Where(i => i.JobId.HasValue)
            .GroupBy(i => i.JobId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        return divisions.Select(d =>
        {
            var divisionJobs = jobs.Where(j => j.DivisionId == d.Id).ToList();
            var active = divisionJobs.Where(j =>
                j.Status is JobStatus.Scheduled or JobStatus.InProgress or JobStatus.OnHold).ToList();
            var ready = divisionJobs.Where(j => j.IsReadyToInvoice()).ToList();

            return new DivisionScorecardRow
            {
                DivisionId = d.Id,
                DivisionCode = d.Code,
                DivisionName = d.Name,
                ActiveJobs = active.Count,
                AvgProgressPercent = active.Count == 0
                    ? 0m
                    : Math.Round((decimal)active.Average(j => j.GetProgressPercent()), 1),
                ReadyToInvoiceCount = ready.Count,
                ReadyToInvoiceValue = ready.Sum(j => j.QuotedTotal),
                InvoicedRevenue = divisionJobs
                    .Where(j => revenueByJob.ContainsKey(j.Id))
                    .Sum(j => revenueByJob[j.Id])
            };
        }).ToList();
    }

    public async Task<string> ExportDivisionScorecardsCsvAsync(CancellationToken ct = default)
    {
        var rows = await GetDivisionScorecardsAsync(ct);
        return TabularExportHelper.BuildCsv(
            "DivisionCode,DivisionName,ActiveJobs,AvgProgressPercent,ReadyToInvoiceCount,ReadyToInvoiceValue,InvoicedRevenue",
            rows.Select(r => new[]
            {
                r.DivisionCode,
                r.DivisionName,
                r.ActiveJobs.ToString(),
                r.AvgProgressPercent.ToString("0.0"),
                r.ReadyToInvoiceCount.ToString(),
                TabularExportHelper.FormatDecimal(r.ReadyToInvoiceValue),
                TabularExportHelper.FormatDecimal(r.InvoicedRevenue)
            }));
    }

    public async Task<IReadOnlyList<UserActivityRow>> GetUserActivityAsync(int days = 30, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Max(1, days));
        var entries = await _dbContext.Set<AuditLogEntry>()
            .AsNoTracking()
            .Where(e => e.OccurredAtUtc >= since)
            .ToListAsync(ct);

        static bool IsApprovalAction(string action) =>
            action.Contains("APPROVE", StringComparison.OrdinalIgnoreCase)
            || action.Contains("SIGN", StringComparison.OrdinalIgnoreCase)
            || action.Contains("RECEIVE", StringComparison.OrdinalIgnoreCase)
            || action.Contains("STATUS", StringComparison.OrdinalIgnoreCase);

        return entries
            .GroupBy(e => e.UserEmail)
            .Select(g => new UserActivityRow
            {
                UserEmail = g.Key,
                TotalActions = g.Count(),
                ApprovalActions = g.Count(e => IsApprovalAction(e.Action)),
                LastActivityUtc = g.Max(e => e.OccurredAtUtc)
            })
            .OrderByDescending(r => r.TotalActions)
            .ToList();
    }

    public async Task<string> ExportUserActivityCsvAsync(int days = 30, CancellationToken ct = default)
    {
        var rows = await GetUserActivityAsync(days, ct);
        return TabularExportHelper.BuildCsv(
            "UserEmail,TotalActions,ApprovalActions,LastActivityUtc",
            rows.Select(r => new[]
            {
                r.UserEmail,
                r.TotalActions.ToString(),
                r.ApprovalActions.ToString(),
                r.LastActivityUtc?.ToString("yyyy-MM-dd HH:mm") ?? ""
            }));
    }

    public async Task<IReadOnlyList<OverdueApprovalRow>> GetOverdueApprovalsAsync(CancellationToken ct = default)
    {
        var slaHours = await GetTenantSlaHoursAsync(ct);
        var now = DateTime.UtcNow;
        var rows = new List<OverdueApprovalRow>();

        var pendingQuotes = (await _dbContext.Set<Quote>()
            .AsNoTracking()
            .Include(q => q.Customer)
            .ToListAsync(ct))
            .Where(q => q.ApprovalStatus == QuoteApprovalStatus.PendingExecutive);

        foreach (var quote in pendingQuotes.Where(q => q.SubmittedForApprovalAt.HasValue))
        {
            var submitted = quote.SubmittedForApprovalAt!.Value;
            var hours = (int)Math.Floor((now - submitted).TotalHours);
            if (hours >= slaHours)
            {
                rows.Add(new OverdueApprovalRow
                {
                    ItemType = "Quote",
                    Reference = quote.QuoteNumber,
                    Description = quote.Customer?.Name ?? quote.Notes ?? "—",
                    SubmittedAtUtc = submitted,
                    HoursInQueue = hours,
                    SlaHours = slaHours
                });
            }
        }

        var pendingRequisitions = (await _dbContext.Set<StockRequisition>()
            .AsNoTracking()
            .Include(r => r.Job)
            .ToListAsync(ct))
            .Where(r => r.Status == RequisitionStatus.PendingManager || r.Status == RequisitionStatus.PendingExecutive);

        foreach (var req in pendingRequisitions)
        {
            var submitted = req.ManagerApprovedAt ?? req.CreatedDate;
            var hours = (int)Math.Floor((now - submitted).TotalHours);
            if (hours >= slaHours)
            {
                rows.Add(new OverdueApprovalRow
                {
                    ItemType = "Requisition",
                    Reference = req.RequisitionNumber,
                    Description = req.Job?.JobNumber ?? req.Notes ?? "—",
                    SubmittedAtUtc = submitted,
                    HoursInQueue = hours,
                    SlaHours = slaHours
                });
            }
        }

        var pendingLeave = (await _dbContext.Set<LeaveRequest>()
            .AsNoTracking()
            .Include(l => l.Employee)
            .ToListAsync(ct))
            .Where(l => l.Status == LeaveRequestStatus.PendingManager
                || l.Status == LeaveRequestStatus.PendingExecutive
                || l.Status == LeaveRequestStatus.PendingHr);

        foreach (var leave in pendingLeave)
        {
            var submitted = leave.ManagerApprovedAt ?? leave.CreatedDate;
            var hours = (int)Math.Floor((now - submitted).TotalHours);
            if (hours >= slaHours)
            {
                rows.Add(new OverdueApprovalRow
                {
                    ItemType = "Leave",
                    Reference = leave.Employee?.EmployeeNumber ?? leave.Id.ToString()[..8],
                    Description = leave.Employee == null
                        ? "—"
                        : $"{leave.Employee.FirstName} {leave.Employee.LastName}".Trim(),
                    SubmittedAtUtc = submitted,
                    HoursInQueue = hours,
                    SlaHours = slaHours
                });
            }
        }

        var pendingFieldReports = (await _dbContext.Set<FieldReport>()
            .AsNoTracking()
            .Include(f => f.Job)
            .ToListAsync(ct))
            .Where(f => f.Status == FieldReportStatus.PendingApproval);

        foreach (var report in pendingFieldReports)
        {
            var submitted = report.CreatedDate;
            var hours = (int)Math.Floor((now - submitted).TotalHours);
            if (hours >= slaHours)
            {
                rows.Add(new OverdueApprovalRow
                {
                    ItemType = "FieldReport",
                    Reference = report.Job?.JobNumber ?? report.Id.ToString()[..8],
                    Description = report.Comments ?? report.MaterialsUsed ?? "—",
                    SubmittedAtUtc = submitted,
                    HoursInQueue = hours,
                    SlaHours = slaHours
                });
            }
        }

        return rows
            .OrderByDescending(r => r.HoursInQueue)
            .ToList();
    }

    public async Task<string> ExportOverdueApprovalsCsvAsync(CancellationToken ct = default)
    {
        var rows = await GetOverdueApprovalsAsync(ct);
        return TabularExportHelper.BuildCsv(
            "ItemType,Reference,Description,SubmittedAtUtc,HoursInQueue,SlaHours",
            rows.Select(r => new[]
            {
                r.ItemType,
                r.Reference,
                r.Description,
                r.SubmittedAtUtc.ToString("yyyy-MM-dd HH:mm"),
                r.HoursInQueue.ToString(),
                r.SlaHours.ToString()
            }));
    }

    private async Task<int> GetTenantSlaHoursAsync(CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == Guid.Empty)
            return 48;

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted, ct);

        return tenant is { DefaultApprovalSlaHours: > 0 } ? tenant.DefaultApprovalSlaHours : 48;
    }
}