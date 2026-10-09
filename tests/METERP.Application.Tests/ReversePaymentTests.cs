using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ReversePaymentTests
{
    [Fact]
    public async Task ReversePaymentAsync_RestoresBalanceStatusAndStatement_AndAudits()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb($"rev-pay-{Guid.NewGuid():N}", tenantId);
        var audit = new Mock<IAuditService>();
        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new InvoiceService(db, auditService: audit.Object, notifications: notifications.Object);

        var customer = new Customer { Name = "Bushing Co" };
        db.Set<Customer>().Add(customer);
        var invoice = new Invoice
        {
            CustomerId = customer.Id,
            InvoiceNumber = "INV-REV-1",
            Status = InvoiceStatus.Sent,
            DocumentType = InvoiceDocumentType.Standard,
            DueDate = DateTime.UtcNow.Date.AddDays(14),
            Subtotal = 1000m,
            Tax = 150m,
            Total = 1150m
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var before = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(before);
        Assert.Equal(1150m, before.ClosingBalance);

        await service.RecordPaymentAsync(invoice.Id, 1150m, DateTime.UtcNow.Date, "EFT-77", null, "bank");
        var paid = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, paid.Status);
        Assert.Equal(1150m, paid.AmountPaid);

        var settled = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(settled);
        Assert.Equal(0m, settled.ClosingBalance);

        var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == invoice.Id);
        await service.ReversePaymentAsync(payment.Id, "Paid the wrong customer");

        var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(0m, saved.AmountPaid);
        Assert.Equal(InvoiceStatus.Sent, saved.Status);

        var hidden = await db.Set<InvoicePayment>().SingleOrDefaultAsync(p => p.Id == payment.Id);
        Assert.Null(hidden);
        var reversed = await db.Set<InvoicePayment>().IgnoreQueryFilters().SingleAsync(p => p.Id == payment.Id);
        Assert.True(reversed.IsDeleted);
        Assert.Contains("Paid the wrong customer", reversed.Notes);

        var payments = await service.GetPaymentsAsync(invoice.Id);
        Assert.Empty(payments);

        var after = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(after);
        Assert.Equal(1150m, after.ClosingBalance);
        Assert.DoesNotContain(after.Lines, line => line.Kind == "Receipt");

        audit.Verify(a => a.LogAsync(
            "REVERSE",
            "Invoice",
            "INV-REV-1",
            It.Is<string>(d => d.Contains("Paid the wrong customer") && d.Contains(1150m.ToString("N2")) && d.Contains("EFT-77")),
            It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.CreateAsync(
            It.Is<TenantNotification>(t =>
                t.Category == "collections"
                && t.RelatedEntityId == invoice.Id
                && t.Title.Contains("reversed", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Once);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReversePaymentAsync(payment.Id, "Paid the wrong customer"));
        Assert.Equal("This receipt has already been reversed.", again.Message);

        var still = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(0m, still.AmountPaid);
        Assert.Equal(InvoiceStatus.Sent, still.Status);
    }

    [Fact]
    public async Task ReversePaymentAsync_RestoresPartiallyPaidWhenAnotherReceiptRemains()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Split Co" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceNumber = "INV-PART",
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(7),
                Total = 1000m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 400m, DateTime.UtcNow.Date, "A", null, null);
            await service.RecordPaymentAsync(invoice.Id, 600m, DateTime.UtcNow.Date, "B", null, null);
            var first = await db.Set<InvoicePayment>().SingleAsync(p => p.Reference == "A");

            await service.ReversePaymentAsync(first.Id, "First EFT bounced");

            var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(600m, saved.AmountPaid);
            Assert.Equal(InvoiceStatus.PartiallyPaid, saved.Status);
            var left = await service.GetPaymentsAsync(invoice.Id);
            var remaining = Assert.Single(left);
            Assert.Equal("B", remaining.Reference);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_RestoresOverdueWhenNothingRemainsAndTheDueDateHasPassed()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Late Co" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceNumber = "INV-LATE",
                Status = InvoiceStatus.Overdue,
                DueDate = DateTime.UtcNow.Date.AddDays(-12),
                Total = 800m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 800m, DateTime.UtcNow.Date, "LATE", null, null);
            var paid = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(InvoiceStatus.Paid, paid.Status);

            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == invoice.Id);
            await service.ReversePaymentAsync(payment.Id, "Allocated to the wrong invoice");

            var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(0m, saved.AmountPaid);
            Assert.Equal(InvoiceStatus.Overdue, saved.Status);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_RequiresAReason_AndLeavesTheReceipt()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var invoice = await SeedPaidInvoiceAsync(db, service, "INV-REASON", 200m);
            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == invoice.Id);

            var blank = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReversePaymentAsync(payment.Id, "  "));
            Assert.Equal("A reason is required to reverse a receipt.", blank.Message);

            var shortReason = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReversePaymentAsync(payment.Id, "no"));
            Assert.Equal("Reversal reason must be at least 3 characters.", shortReason.Message);

            var longReason = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ReversePaymentAsync(payment.Id, new string('R', 501)));
            Assert.Equal("Reversal reason cannot exceed 500 characters.", longReason.Message);

            var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(200m, saved.AmountPaid);
            Assert.Equal(InvoiceStatus.Paid, saved.Status);
            Assert.False((await db.Set<InvoicePayment>().SingleAsync(p => p.Id == payment.Id)).IsDeleted);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_RefusesAnotherTenant()
    {
        var database = $"rev-tenant-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid paymentId;
        Guid invoiceId;

        await using (var dbB = CreateDb(database, tenantB))
        {
            var serviceB = new InvoiceService(dbB);
            var invoice = await SeedPaidInvoiceAsync(dbB, serviceB, "INV-B", 450m);
            invoiceId = invoice.Id;
            paymentId = (await dbB.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == invoice.Id)).Id;
        }

        await using var dbA = CreateDb(database, tenantA);
        var serviceA = new InvoiceService(dbA);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceA.ReversePaymentAsync(paymentId, "Not our customer"));
        Assert.Equal("Cannot reverse a payment on another tenant.", denied.Message);

        await using var dbBAgain = CreateDb(database, tenantB);
        var untouched = await dbBAgain.Set<Invoice>().IgnoreQueryFilters().SingleAsync(i => i.Id == invoiceId);
        Assert.Equal(450m, untouched.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, untouched.Status);
        var receipt = await dbBAgain.Set<InvoicePayment>().IgnoreQueryFilters().SingleAsync(p => p.Id == paymentId);
        Assert.False(receipt.IsDeleted);
    }

    [Fact]
    public async Task ReversePaymentAsync_ClearsDepositReceived_WhenNoOtherCountingDepositRemains()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Deposit Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                CustomerId = customer.Id,
                JobNumber = "J-DEP",
                Title = "Transformer",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                DepositReceived = false,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var deposit = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-REV",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 3000m
            };
            var draftSibling = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-DRAFT",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Draft,
                Total = 3000m
            };
            var cancelledSibling = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-CAN",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Cancelled,
                Total = 3000m
            };
            db.Set<Invoice>().AddRange(deposit, draftSibling, cancelledSibling);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(deposit.Id, 3000m, DateTime.UtcNow.Date, "DEP", null, null);
            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);

            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == deposit.Id);
            await service.ReversePaymentAsync(payment.Id, "Deposit paid from the wrong account");

            var savedJob = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.False(savedJob.DepositReceived);
            var savedInvoice = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == deposit.Id);
            Assert.Equal(0m, savedInvoice.AmountPaid);
            Assert.Equal(InvoiceStatus.Sent, savedInvoice.Status);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_KeepsDepositReceived_WhenAnotherCountingDepositRemains()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Two Dep" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                CustomerId = customer.Id,
                JobNumber = "J-DEP-2",
                Title = "Second deposit",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var paidDeposit = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-A",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 1500m
            };
            var openDeposit = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-B",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 1500m
            };
            db.Set<Invoice>().AddRange(paidDeposit, openDeposit);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(paidDeposit.Id, 1500m, DateTime.UtcNow.Date, "A", null, null);
            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);

            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == paidDeposit.Id);
            await service.ReversePaymentAsync(payment.Id, "This deposit receipt was duplicated");

            var savedJob = await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id);
            Assert.True(savedJob.DepositReceived);
            var savedInvoice = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == paidDeposit.Id);
            Assert.Equal(0m, savedInvoice.AmountPaid);
            Assert.True(savedInvoice.AmountPaid < savedInvoice.Total);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_KeepsDepositReceived_WhenTheDepositIsStillFullyPaid()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Covered Dep" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                CustomerId = customer.Id,
                JobNumber = "J-DEP-3",
                Title = "Still covered",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                DepositReceived = true,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var deposit = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-COVER",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Paid,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 3000m,
                AmountPaid = 4500m
            };
            db.Set<Invoice>().Add(deposit);
            db.Set<InvoicePayment>().Add(new InvoicePayment
            {
                InvoiceId = deposit.Id,
                Amount = 500m,
                PaymentDate = DateTime.UtcNow.Date,
                Reference = "EXTRA"
            });
            await db.SaveChangesAsync();

            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == deposit.Id);
            await service.ReversePaymentAsync(payment.Id, "Extra receipt on a covered deposit");

            var savedInvoice = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == deposit.Id);
            Assert.Equal(4000m, savedInvoice.AmountPaid);
            Assert.Equal(InvoiceStatus.Paid, savedInvoice.Status);
            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);
        }
    }

    [Fact]
    public async Task ReversePaymentAsync_DoesNotClearDepositReceived_ForAStandardInvoice()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = new Customer { Name = "Progress Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                CustomerId = customer.Id,
                JobNumber = "J-STD",
                Title = "Progress bill",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                DepositReceived = true,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var invoice = new Invoice
            {
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "INV-STD",
                DocumentType = InvoiceDocumentType.Standard,
                Status = InvoiceStatus.Sent,
                DueDate = DateTime.UtcNow.Date.AddDays(10),
                Total = 2000m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 2000m, DateTime.UtcNow.Date, "STD", null, null);
            var payment = await db.Set<InvoicePayment>().SingleAsync(p => p.InvoiceId == invoice.Id);
            await service.ReversePaymentAsync(payment.Id, "Progress receipt was entered twice");

            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);
            var saved = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            Assert.Equal(0m, saved.AmountPaid);
            Assert.Equal(InvoiceStatus.Sent, saved.Status);
        }
    }

    private static async Task<Invoice> SeedPaidInvoiceAsync(
        AppDbContext db,
        InvoiceService service,
        string number,
        decimal total)
    {
        var customer = new Customer { Name = number };
        db.Set<Customer>().Add(customer);
        var invoice = new Invoice
        {
            CustomerId = customer.Id,
            InvoiceNumber = number,
            Status = InvoiceStatus.Sent,
            DueDate = DateTime.UtcNow.Date.AddDays(20),
            Total = total
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();
        await service.RecordPaymentAsync(invoice.Id, total, DateTime.UtcNow.Date, "EFT", null, null);
        return invoice;
    }

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"rev-pay-{Guid.NewGuid():N}", tenantId);
        return (new InvoiceService(db), db, tenantId);
    }

    private static AppDbContext CreateDb(string database, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .Options;
        return new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
    }
}
