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

    [Fact]
    public async Task RunOverdueInvoiceScanAsync_CreatesAlertForUnpaidPastDueInvoice()
    {
        var (service, db, tenantId, notifications, audit) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Slow Pay Ltd" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-OVER",
                DocumentType = InvoiceDocumentType.Standard,
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(-5),
                Total = 2500m,
                AmountPaid = 0m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var created = await service.RunOverdueInvoiceScanAsync();

            Assert.Equal(1, created);
            notifications.Verify(n => n.CreateAsync(
                It.Is<TenantNotification>(t =>
                    t.Category == "collections"
                    && t.TargetRoles.Contains("Executive")
                    && t.RelatedEntityId == invoice.Id
                    && t.Message.Contains("Slow Pay")),
                It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(a => a.LogAsync("COLLECTIONS_SCAN", "Invoice", "overdue-alerts", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task RunOverdueInvoiceScanAsync_SkipsWhenNotificationAlreadyExists()
    {
        var (service, db, tenantId, notifications, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Known" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-DUP",
                Status = InvoiceStatus.Overdue,
                DueDate = DateTime.UtcNow.Date.AddDays(-3),
                Total = 800m
            };
            db.Set<Invoice>().Add(invoice);
            db.Set<TenantNotification>().Add(new TenantNotification
            {
                TenantId = tenantId,
                Title = "Invoice INV-DUP is 3 day(s) overdue",
                Message = "Already sent",
                Category = "collections",
                RelatedEntityId = invoice.Id,
                RelatedEntityType = nameof(Invoice)
            });
            await db.SaveChangesAsync();

            var created = await service.RunOverdueInvoiceScanAsync();

            Assert.Equal(0, created);
            notifications.Verify(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task RunOverdueInvoiceScanAsync_SkipsProformaAndPaid()
    {
        var (service, db, tenantId, notifications, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Skip Co" };
            db.Set<Customer>().Add(customer);
            db.Set<Invoice>().AddRange(
                new Invoice
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    InvoiceNumber = "PRO-OLD",
                    DocumentType = InvoiceDocumentType.Proforma,
                    Status = InvoiceStatus.Sent,
                    DueDate = DateTime.UtcNow.Date.AddDays(-10),
                    Total = 1000m
                },
                new Invoice
                {
                    TenantId = tenantId,
                    CustomerId = customer.Id,
                    InvoiceNumber = "INV-PAID",
                    Status = InvoiceStatus.Paid,
                    DueDate = DateTime.UtcNow.Date.AddDays(-10),
                    Total = 1000m,
                    AmountPaid = 1000m
                });
            await db.SaveChangesAsync();

            var created = await service.RunOverdueInvoiceScanAsync();

            Assert.Equal(0, created);
            notifications.Verify(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task RunApprovalSlaScanAsync_CreatesAlertForStalePendingQuote()
    {
        var (service, db, tenantId, notifications, audit) = Create();
        await using (db)
        {
            db.Set<Tenant>().Add(new Tenant
            {
                Id = tenantId,
                Name = "SLA Co",
                Subdomain = "sla",
                DefaultApprovalSlaHours = 48
            });
            var customer = new Customer { TenantId = tenantId, Name = "Wait Co" };
            db.Set<Customer>().Add(customer);
            var quote = new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-SLA",
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                SubmittedForApprovalAt = DateTime.UtcNow.AddHours(-60)
            };
            db.Set<Quote>().Add(quote);
            await db.SaveChangesAsync();

            var created = await service.RunApprovalSlaScanAsync();

            Assert.Equal(1, created);
            notifications.Verify(n => n.CreateAsync(
                It.Is<TenantNotification>(t =>
                    t.Category == "approvals"
                    && t.RelatedEntityType == nameof(Quote)
                    && t.RelatedEntityId == quote.Id
                    && t.Title.Contains("Q-SLA")),
                It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(a => a.LogAsync("SLA_SCAN", "Approval", "sla-alerts", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task RunApprovalSlaScanAsync_SkipsFreshPendingQuote()
    {
        var (service, db, tenantId, notifications, _) = Create();
        await using (db)
        {
            db.Set<Tenant>().Add(new Tenant
            {
                Id = tenantId,
                Name = "SLA Co",
                Subdomain = "sla-fresh",
                DefaultApprovalSlaHours = 48
            });
            var customer = new Customer { TenantId = tenantId, Name = "Fast Co" };
            db.Set<Customer>().Add(customer);
            db.Set<Quote>().Add(new Quote
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuoteNumber = "Q-FRESH",
                ApprovalStatus = QuoteApprovalStatus.PendingExecutive,
                SubmittedForApprovalAt = DateTime.UtcNow.AddHours(-2)
            });
            await db.SaveChangesAsync();

            var created = await service.RunApprovalSlaScanAsync();

            Assert.Equal(0, created);
            notifications.Verify(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}