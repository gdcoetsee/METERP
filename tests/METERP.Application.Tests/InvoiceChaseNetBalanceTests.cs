using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceChaseNetBalanceTests
{
    [Fact]
    public async Task ChaseOverdueAsync_LinkedCredit_StatesNetBalanceAndAudits()
    {
        var (service, db, tenantId, email, audit, notifications) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Net Co", Email = "ap@net.co" };
            db.Set<Customer>().Add(customer);
            var invoice = OverdueInvoice(tenantId, customer.Id, "INV-NET", total: 1000m, paid: 200m, daysOverdue: 12);
            db.Set<Invoice>().Add(invoice);
            db.Set<Invoice>().Add(OpenCredit(tenantId, customer.Id, "CRN-NET", 300m, invoice.Id));
            await db.SaveChangesAsync();

            var result = await service.ChaseOverdueAsync(invoice.Id);

            var netText = $"R {500m:N2}";
            var grossText = $"R {800m:N2}";
            Assert.Equal(500m, result.BalanceDue);
            Assert.True(result.EmailSent);
            email.Verify(e => e.SendEmailAsync(
                "ap@net.co",
                It.Is<string>(s => s.Contains("INV-NET")),
                It.Is<string>(b => b.Contains(netText) && !b.Contains(grossText)),
                It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(a => a.LogAsync(
                "CHASE",
                "Invoice",
                "INV-NET",
                It.Is<string>(m => m.Contains(netText) && !m.Contains(grossText)),
                It.IsAny<CancellationToken>()), Times.Once);
            notifications.Verify(n => n.CreateAsync(
                It.Is<TenantNotification>(t => t.Message.Contains(netText) && !t.Message.Contains(grossText)),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task ChaseOverdueAsync_UnlinkedCredit_StaysOnTheOlderInvoice()
    {
        var (service, db, tenantId, _, _, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Older Co", Email = "ap@older.co" };
            db.Set<Customer>().Add(customer);
            var older = OverdueInvoice(tenantId, customer.Id, "INV-OLD", total: 1000m, paid: 0m, daysOverdue: 40);
            var current = OverdueInvoice(tenantId, customer.Id, "INV-NEW", total: 1000m, paid: 200m, daysOverdue: 10);
            db.Set<Invoice>().AddRange(older, current);
            db.Set<Invoice>().Add(OpenCredit(tenantId, customer.Id, "CRN-FREE", 300m, parentInvoiceId: null));
            await db.SaveChangesAsync();

            var result = await service.ChaseOverdueAsync(current.Id);

            Assert.Equal(800m, result.BalanceDue);
        }
    }

    [Fact]
    public async Task ChaseOverdueAsync_RefusesPaidInvoice_AndDoesNotAudit()
    {
        var (service, db, tenantId, email, audit, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Paid Co", Email = "ap@paid.co" };
            db.Set<Customer>().Add(customer);
            var invoice = OverdueInvoice(tenantId, customer.Id, "INV-PAID", total: 1000m, paid: 1000m, daysOverdue: 9);
            invoice.Status = InvoiceStatus.Paid;
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChaseOverdueAsync(invoice.Id));

            Assert.Contains("paid", ex.Message, StringComparison.OrdinalIgnoreCase);
            email.Verify(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            audit.Verify(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task ChaseOverdueAsync_RefusesWhenOpenCreditCoversTheBalance_AndDoesNotAudit()
    {
        var (service, db, tenantId, email, audit, _) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Covered Co", Email = "ap@covered.co" };
            db.Set<Customer>().Add(customer);
            var invoice = OverdueInvoice(tenantId, customer.Id, "INV-ZERO", total: 1000m, paid: 200m, daysOverdue: 8);
            db.Set<Invoice>().Add(invoice);
            db.Set<Invoice>().Add(OpenCredit(tenantId, customer.Id, "CRN-COVER", 800m, invoice.Id));
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChaseOverdueAsync(invoice.Id));

            Assert.Contains("nothing left", ex.Message, StringComparison.OrdinalIgnoreCase);
            email.Verify(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            audit.Verify(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            var saved = await db.Set<Invoice>().FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(InvoiceStatus.Sent, saved.Status);
            Assert.True(string.IsNullOrEmpty(saved.Notes));
        }
    }

    [Fact]
    public async Task ChaseOverdueAsync_IgnoresDraftDeletedAndOtherTenantCredits()
    {
        var dbName = $"chase-net-{Guid.NewGuid():N}";
        var (service, db, tenantId, _, _, _) = Create(dbName);
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Filter Co", Email = "ap@filter.co" };
            db.Set<Customer>().Add(customer);
            var invoice = OverdueInvoice(tenantId, customer.Id, "INV-FILT", total: 1000m, paid: 200m, daysOverdue: 6);
            db.Set<Invoice>().Add(invoice);
            var draft = OpenCredit(tenantId, customer.Id, "CRN-DRAFT", 300m, invoice.Id);
            draft.Status = InvoiceStatus.Draft;
            var deleted = OpenCredit(tenantId, customer.Id, "CRN-DEL", 300m, invoice.Id);
            deleted.IsDeleted = true;
            db.Set<Invoice>().AddRange(draft, deleted);
            await db.SaveChangesAsync();

            var otherTenant = Guid.NewGuid();
            await using (var other = CreateContext(dbName, otherTenant))
            {
                var otherCustomer = new Customer { TenantId = otherTenant, Name = "Other Co" };
                other.Set<Customer>().Add(otherCustomer);
                await other.SaveChangesAsync();
                other.Set<Invoice>().Add(OpenCredit(otherTenant, otherCustomer.Id, "CRN-OTHER", 300m, invoice.Id));
                await other.SaveChangesAsync();
            }

            var result = await service.ChaseOverdueAsync(invoice.Id);

            Assert.Equal(800m, result.BalanceDue);
        }
    }

    private static Invoice OverdueInvoice(
        Guid tenantId,
        Guid customerId,
        string number,
        decimal total,
        decimal paid,
        int daysOverdue) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.Standard,
            Status = InvoiceStatus.Sent,
            InvoiceDate = DateTime.UtcNow.Date.AddDays(-daysOverdue - 30),
            DueDate = DateTime.UtcNow.Date.AddDays(-daysOverdue),
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = paid
        };

    private static Invoice OpenCredit(
        Guid tenantId,
        Guid customerId,
        string number,
        decimal total,
        Guid? parentInvoiceId) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customerId,
            InvoiceNumber = number,
            DocumentType = InvoiceDocumentType.CreditNote,
            Status = InvoiceStatus.Sent,
            InvoiceDate = DateTime.UtcNow.Date.AddDays(-1),
            DueDate = DateTime.UtcNow.Date.AddDays(-1),
            Subtotal = total,
            Tax = 0m,
            Total = total,
            AmountPaid = 0m,
            CreditNoteForInvoiceId = parentInvoiceId
        };

    private static (
        InvoiceService Service,
        AppDbContext Db,
        Guid TenantId,
        Mock<IEmailSender> Email,
        Mock<IAuditService> Audit,
        Mock<ITenantNotificationService> Notifications) Create(string? dbName = null)
    {
        var tenantId = Guid.NewGuid();
        var db = CreateContext(dbName ?? $"chase-net-{Guid.NewGuid():N}", tenantId);
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.IsConfigured).Returns(true);
        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new InvoiceService(db, auditService: audit.Object, email: email.Object, notifications: notifications.Object);
        return (service, db, tenantId, email, audit, notifications);
    }

    private static AppDbContext CreateContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
    }
}
