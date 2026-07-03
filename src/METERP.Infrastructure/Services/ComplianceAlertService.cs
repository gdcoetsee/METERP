using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public sealed class ComplianceAlertService : IComplianceAlertService
{
    private const string ComplianceRoles = "Executive,HrManager";

    private readonly AppDbContext _dbContext;
    private readonly ITenantNotificationService _notifications;
    private readonly IAuditService? _audit;

    public ComplianceAlertService(
        AppDbContext dbContext,
        ITenantNotificationService notifications,
        IAuditService? audit = null)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<int> RunExpiryScanAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var alertsCreated = 0;

        alertsCreated += await ScanCompanyDocumentsAsync(now, ct);
        alertsCreated += await ScanEmployeeCertificationsAsync(now, ct);

        if (alertsCreated > 0 && _audit != null)
        {
            await _audit.LogAsync(
                "COMPLIANCE_SCAN",
                "Compliance",
                "expiry-alerts",
                $"Created {alertsCreated} expiry notification(s)",
                ct);
        }

        return alertsCreated;
    }

    private async Task<int> ScanCompanyDocumentsAsync(DateTime now, CancellationToken ct)
    {
        var docs = await _dbContext.Set<CompanyDocument>()
            .Where(d => !d.NoExpiry && d.ExpiryDate != null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var doc in docs)
        {
            var days = ComplianceExpiryCalculator.GetDaysUntilExpiry(doc.ExpiryDate, now);
            if (days is null) continue;

            var threshold = ComplianceExpiryCalculator.GetAlertThresholdToSend(days.Value, doc.LastExpiryAlertDaysRemaining);
            if (threshold is null) continue;

            await _notifications.CreateAsync(new TenantNotification
            {
                Title = $"Company document expiring in {days} day(s)",
                Message = $"{doc.Title} ({doc.DocumentType}) expires on {doc.ExpiryDate:yyyy-MM-dd}. Renew before work is delayed.",
                Category = "compliance",
                TargetRoles = ComplianceRoles,
                RelatedEntityId = doc.Id,
                RelatedEntityType = nameof(CompanyDocument)
            }, ct);

            doc.LastExpiryAlertDaysRemaining = threshold;
            count++;
        }

        if (count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return count;
    }

    private async Task<int> ScanEmployeeCertificationsAsync(DateTime now, CancellationToken ct)
    {
        var certs = await _dbContext.Set<EmployeeCertification>()
            .Include(c => c.Employee)
            .Where(c => !c.NoExpiry && c.ExpiryDate != null)
            .ToListAsync(ct);

        var count = 0;
        foreach (var cert in certs)
        {
            var days = ComplianceExpiryCalculator.GetDaysUntilExpiry(cert.ExpiryDate, now);
            if (days is null) continue;

            var threshold = ComplianceExpiryCalculator.GetAlertThresholdToSend(days.Value, cert.LastExpiryAlertDaysRemaining);
            if (threshold is null) continue;

            var employeeName = cert.Employee != null
                ? $"{cert.Employee.FirstName} {cert.Employee.LastName}"
                : "Employee";

            await _notifications.CreateAsync(new TenantNotification
            {
                Title = $"Employee certification expiring in {days} day(s)",
                Message = $"{employeeName}: {cert.CertificationType} expires on {cert.ExpiryDate:yyyy-MM-dd}.",
                Category = "compliance",
                TargetRoles = ComplianceRoles,
                RelatedEntityId = cert.Id,
                RelatedEntityType = nameof(EmployeeCertification)
            }, ct);

            cert.LastExpiryAlertDaysRemaining = threshold;
            count++;
        }

        if (count > 0)
            await _dbContext.SaveChangesAsync(ct);

        return count;
    }
}