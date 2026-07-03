using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Identity;
using METERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using METERP.Application.Options;

namespace METERP.Infrastructure.Services;

public sealed class ScheduledReportService : IScheduledReportService
{
    private readonly IExecutiveDashboardService _dashboard;
    private readonly IAccountabilityReportService _accountability;
    private readonly IEmailSender _email;
    private readonly UserManager<ApplicationUser>? _userManager;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantProvider _tenantProvider;
    private readonly AppDbContext _dbContext;
    private readonly EmailOptions _emailOptions;

    public ScheduledReportService(
        IExecutiveDashboardService dashboard,
        IAccountabilityReportService accountability,
        IEmailSender email,
        ICurrentUserService currentUser,
        IOptions<EmailOptions> emailOptions,
        ITenantProvider tenantProvider,
        AppDbContext dbContext,
        UserManager<ApplicationUser>? userManager = null)
    {
        _dashboard = dashboard;
        _accountability = accountability;
        _email = email;
        _userManager = userManager;
        _currentUser = currentUser;
        _tenantProvider = tenantProvider;
        _dbContext = dbContext;
        _emailOptions = emailOptions.Value;
    }

    public async Task<bool> SendExecutiveSummaryEmailAsync(string? toEmail = null, CancellationToken ct = default)
    {
        if (!_email.IsConfigured)
            return false;

        var recipient = toEmail?.Trim();
        if (string.IsNullOrWhiteSpace(recipient) && _userManager != null && _currentUser.UserId is { } userId)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            recipient = user?.Email;
        }

        if (string.IsNullOrWhiteSpace(recipient))
            return false;

        var html = await BuildExecutiveSummaryHtmlAsync(ct);
        var branding = await GetCurrentBrandingAsync(ct);
        await _email.SendEmailAsync(recipient, $"{branding.DisplayName} — Executive Accountability Report", html, ct);
        return true;
    }

    public async Task<int> SendScheduledExecutiveReportsAsync(CancellationToken ct = default)
    {
        if (!_email.IsConfigured)
            return 0;

        var tenants = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.IsActive && !t.IsDeleted && t.NotificationEmail != null && t.NotificationEmail != "")
            .ToListAsync(ct);

        var sent = 0;
        foreach (var tenant in tenants)
        {
            _tenantProvider.SetTenantId(tenant.Id);
            if (await SendExecutiveSummaryEmailAsync(tenant.NotificationEmail, ct))
                sent++;
        }

        return sent;
    }

    private async Task<string> BuildExecutiveSummaryHtmlAsync(CancellationToken ct)
    {
        var summary = await _dashboard.GetSummaryAsync(ct);
        var divisions = await _accountability.GetDivisionScorecardsAsync(ct);
        var activity = await _accountability.GetUserActivityAsync(30, ct);
        var overdue = await _accountability.GetOverdueApprovalsAsync(ct);
        var branding = await GetCurrentBrandingAsync(ct);

        var divisionRows = string.Join("<br/>",
            divisions.Take(5).Select(d =>
                $"{d.DivisionName}: {d.ActiveJobs} active, {d.ReadyToInvoiceCount} ready to invoice (R {d.ReadyToInvoiceValue:N0})"));

        var activityRows = string.Join("<br/>",
            activity.Take(5).Select(a => $"{a.UserEmail}: {a.TotalActions} actions ({a.ApprovalActions} approvals)"));

        var overdueRows = overdue.Count == 0
            ? "None — all queues within SLA."
            : string.Join("<br/>", overdue.Take(8).Select(o =>
                $"{o.ItemType} {o.Reference}: {o.HoursInQueue}h in queue (SLA {o.SlaHours}h)"));

        return $"""
            <h2>{branding.DisplayName} — Executive Summary</h2>
            <p><strong>Pending approvals:</strong> {summary.PendingApprovals}</p>
            <p><strong>Overdue (SLA breach):</strong> {overdue.Count}</p>
            <p><strong>Ready to invoice:</strong> {summary.ReadyToInvoiceJobs} jobs (R {summary.ReadyToInvoiceValue:N0})</p>
            <p><strong>Outstanding receivables:</strong> R {summary.AgedDebtorsTotal:N0}</p>
            <h3>Overdue approval queue</h3>
            <p>{overdueRows}</p>
            <h3>Division scorecards</h3>
            <p>{divisionRows}</p>
            <h3>User activity (30 days)</h3>
            <p>{activityRows}</p>
            <p><em>Sent from {_emailOptions.FromName} — paperless accountability report.</em></p>
            """;
    }

    private async Task<TenantBranding> GetCurrentBrandingAsync(CancellationToken ct)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId == Guid.Empty)
            return TenantBranding.Default;

        var tenant = await _dbContext.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        return TenantBranding.From(tenant);
    }
}