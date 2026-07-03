using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ComplianceAlertServiceTests
{
    private static (ComplianceAlertService Service, AppDbContext Db, Guid TenantId, Mock<ITenantNotificationService> Notifications, Mock<IAuditService> Audit) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"compliance-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        var service = new ComplianceAlertService(db, notifications.Object, audit.Object);
        return (service, db, tenantId, notifications, audit);
    }

    [Fact]
    public async Task RunExpiryScanAsync_CreatesCompanyDocumentAlertAtThreshold()
    {
        var (service, db, tenantId, notifications, audit) = Create();
        await using (db)
        {
            var doc = new CompanyDocument
            {
                TenantId = tenantId,
                DocumentType = "COID",
                Title = "COID 2026",
                ExpiryDate = DateTime.UtcNow.Date.AddDays(14),
                LastExpiryAlertDaysRemaining = null
            };
            db.Set<CompanyDocument>().Add(doc);
            await db.SaveChangesAsync();

            var created = await service.RunExpiryScanAsync();

            Assert.Equal(1, created);
            notifications.Verify(n => n.CreateAsync(
                It.Is<TenantNotification>(t =>
                    t.Category == "compliance" &&
                    t.TargetRoles.Contains("Executive") &&
                    t.RelatedEntityId == doc.Id),
                It.IsAny<CancellationToken>()), Times.Once);

            var saved = await db.Set<CompanyDocument>().FirstAsync(d => d.Id == doc.Id);
            Assert.Equal(30, saved.LastExpiryAlertDaysRemaining);
            audit.Verify(a => a.LogAsync("COMPLIANCE_SCAN", "Compliance", "expiry-alerts", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task RunExpiryScanAsync_SkipsWhenThresholdAlreadySent()
    {
        var (service, db, tenantId, notifications, _) = Create();
        await using (db)
        {
            db.Set<CompanyDocument>().Add(new CompanyDocument
            {
                TenantId = tenantId,
                DocumentType = "Tax",
                Title = "Tax clearance",
                ExpiryDate = DateTime.UtcNow.Date.AddDays(10),
                LastExpiryAlertDaysRemaining = 14
            });
            await db.SaveChangesAsync();

            var created = await service.RunExpiryScanAsync();

            Assert.Equal(0, created);
            notifications.Verify(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task RunExpiryScanAsync_CreatesEmployeeCertificationAlert()
    {
        var (service, db, tenantId, notifications, _) = Create();
        await using (db)
        {
            var employee = new Employee
            {
                TenantId = tenantId,
                EmployeeNumber = "E-99",
                FirstName = "Sam",
                LastName = "Tech",
                HireDate = DateTime.UtcNow.AddYears(-2)
            };
            db.Set<Employee>().Add(employee);
            await db.SaveChangesAsync();

            var cert = new EmployeeCertification
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                CertificationType = "Wireman's Licence",
                ExpiryDate = DateTime.UtcNow.Date.AddDays(7),
                LastExpiryAlertDaysRemaining = 14
            };
            db.Set<EmployeeCertification>().Add(cert);
            await db.SaveChangesAsync();

            var created = await service.RunExpiryScanAsync();

            Assert.Equal(1, created);
            notifications.Verify(n => n.CreateAsync(
                It.Is<TenantNotification>(t =>
                    t.Title.Contains("7 day") &&
                    t.Message.Contains("Wireman's Licence") &&
                    t.RelatedEntityType == nameof(EmployeeCertification)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}