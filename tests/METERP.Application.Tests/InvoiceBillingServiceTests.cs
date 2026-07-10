using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class InvoiceBillingServiceTests
{
    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"billing-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        return (new InvoiceService(db), db, tenantId);
    }

    [Fact]
    public async Task CreateFromJobAsync_RequiresSignOff()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Gate Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuotedTotal = 1000m,
                Title = "Unsigned"
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateFromJobAsync(job.Id));
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_UpdatesAmountPaidAndStatus()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Pay Co" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-PAY-1",
                Status = InvoiceStatus.Sent,
                Subtotal = 1000,
                Tax = 150,
                Total = 1150
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 500m, DateTime.UtcNow, "EFT-001", Guid.NewGuid(), null);

            var saved = await db.Set<Invoice>().FirstAsync(i => i.Id == invoice.Id);
            Assert.Equal(500m, saved.AmountPaid);
            Assert.Equal(InvoiceStatus.PartiallyPaid, saved.Status);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_OnDeposit_MarksJobDepositReceivedWhenFullyPaid()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Dep Pay Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                Title = "Site work",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                DepositReceived = false,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var invoice = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                JobId = job.Id,
                InvoiceNumber = "DEP-1",
                DocumentType = InvoiceDocumentType.Deposit,
                Status = InvoiceStatus.Sent,
                Subtotal = 3000,
                Tax = 0,
                Total = 3000
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 1500m, DateTime.UtcNow, "half", Guid.NewGuid(), null);
            var mid = await db.Set<Job>().AsNoTracking().FirstAsync(j => j.Id == job.Id);
            Assert.False(mid.DepositReceived);

            await service.RecordPaymentAsync(invoice.Id, 1500m, DateTime.UtcNow, "rest", Guid.NewGuid(), null);
            var done = await db.Set<Job>().AsNoTracking().FirstAsync(j => j.Id == job.Id);
            Assert.True(done.DepositReceived);
        }
    }

    [Fact]
    public async Task OpenPaymentPopAsync_ReturnsNull_WhenNoPop()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "No Pop" };
            db.Set<Customer>().Add(customer);
            var invoice = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-NP",
                Status = InvoiceStatus.Sent,
                Total = 100
            };
            db.Set<Invoice>().Add(invoice);
            var payment = new InvoicePayment
            {
                TenantId = tenantId,
                InvoiceId = invoice.Id,
                Amount = 50,
                PaymentDate = DateTime.UtcNow
            };
            db.Set<InvoicePayment>().Add(payment);
            await db.SaveChangesAsync();

            var result = await service.OpenPaymentPopAsync(payment.Id);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task CreateCreditNoteAsync_CreatesNegativeLines()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "CN Co" };
            db.Set<Customer>().Add(customer);
            var source = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-SRC",
                Subtotal = 1000,
                Tax = 150,
                Total = 1150
            };
            db.Set<Invoice>().Add(source);
            db.Set<InvoiceLine>().Add(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = source.Id,
                Description = "Labour",
                Quantity = 1,
                UnitPrice = 1000
            });
            await db.SaveChangesAsync();

            var creditNote = await service.CreateCreditNoteAsync(source.Id, "Rework credit");

            Assert.Equal(InvoiceDocumentType.CreditNote, creditNote.DocumentType);
            Assert.True(creditNote.Lines.All(l => l.UnitPrice < 0));
            Assert.Equal(source.Id, creditNote.CreditNoteForInvoiceId);
        }
    }

    [Fact]
    public async Task CreateBillingDocumentAsync_DepositUsesJobDepositPercent()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Dep Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuotedTotal = 10000m,
                DepositPercent = 25m,
                Title = "Deposit job"
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var invoice = await service.CreateBillingDocumentAsync(job.Id, InvoiceDocumentType.Deposit);

            Assert.Equal(InvoiceDocumentType.Deposit, invoice.DocumentType);
            Assert.Single(invoice.Lines);
            Assert.Equal(2500m, invoice.Lines.First().UnitPrice);
        }
    }
}