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
    public async Task CreateFromJobAsync_RejectsDeletedCustomer()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Gone Co" };
            db.Set<Customer>().Add(customer);
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuotedTotal = 1000m,
                Title = "Orphan bill",
                SignOffStatus = JobSignOffStatus.SignedOff,
                Status = JobStatus.Completed
            };
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            customer.IsDeleted = true;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateFromJobAsync(job.Id));
            Assert.Contains("customer", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task CreateCreditNoteAsync_RequiresReason()
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
                InvoiceNumber = "INV-R",
                Total = 100
            };
            db.Set<Invoice>().Add(source);
            db.Set<InvoiceLine>().Add(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = source.Id,
                Description = "X",
                Quantity = 1,
                UnitPrice = 100
            });
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateCreditNoteAsync(source.Id, "  "));
        }
    }

    [Fact]
    public async Task CreateCreditNoteAsync_RejectsReasonTooLong()
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
                InvoiceNumber = "INV-LONG",
                Total = 100
            };
            db.Set<Invoice>().Add(source);
            db.Set<InvoiceLine>().Add(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = source.Id,
                Description = "X",
                Quantity = 1,
                UnitPrice = 100
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateCreditNoteAsync(source.Id, new string('R', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCreditNoteAsync_AcceptsReasonAt500Characters()
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
                InvoiceNumber = "INV-CN-OK",
                Status = InvoiceStatus.Sent,
                Total = 100
            };
            db.Set<Invoice>().Add(source);
            db.Set<InvoiceLine>().Add(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = source.Id,
                Description = "X",
                Quantity = 1,
                UnitPrice = 100
            });
            await db.SaveChangesAsync();

            var credit = await service.CreateCreditNoteAsync(source.Id, new string('R', 500));
            Assert.Equal(InvoiceDocumentType.CreditNote, credit.DocumentType);
            Assert.Equal(500, credit.Notes!.Length);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_RejectsNotesTooLong()
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
                InvoiceNumber = "INV-NOTE",
                Status = InvoiceStatus.Sent,
                Subtotal = 500m,
                Tax = 0m,
                Total = 500m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date, null, null, new string('N', 501)));
            Assert.Contains("500 characters", ex.Message);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_AcceptsNotesAt500Characters()
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
                InvoiceNumber = "INV-NOTE-OK",
                Status = InvoiceStatus.Sent,
                Subtotal = 500m,
                Tax = 0m,
                Total = 500m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date, null, null, new string('N', 500));
            var payment = await db.Set<InvoicePayment>().FirstAsync(p => p.InvoiceId == invoice.Id);
            Assert.Equal(500, payment.Notes!.Length);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_RejectsReferenceTooLong()
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
                InvoiceNumber = "INV-REF",
                Status = InvoiceStatus.Sent,
                Subtotal = 500m,
                Tax = 0m,
                Total = 500m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date, new string('R', 101), null, null));
            Assert.Contains("100 characters", ex.Message);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_AcceptsReferenceAt100Characters()
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
                InvoiceNumber = "INV-REF-OK",
                Status = InvoiceStatus.Sent,
                Subtotal = 500m,
                Tax = 0m,
                Total = 500m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            await service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date, new string('R', 100), null, null);
            var payment = await db.Set<InvoicePayment>().FirstAsync(p => p.InvoiceId == invoice.Id);
            Assert.Equal(100, payment.Reference!.Length);
        }
    }

    [Fact]
    public async Task CreateCreditNoteAsync_RejectsShortReason()
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
                InvoiceNumber = "INV-SHORT",
                Total = 100
            };
            db.Set<Invoice>().Add(source);
            db.Set<InvoiceLine>().Add(new InvoiceLine
            {
                TenantId = tenantId,
                InvoiceId = source.Id,
                Description = "X",
                Quantity = 1,
                UnitPrice = 100
            });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateCreditNoteAsync(source.Id, "ab"));
            Assert.Contains("3 characters", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_RejectsIllegalTransitions()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Status Co" };
            db.Set<Customer>().Add(customer);
            var paid = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-PD",
                Status = InvoiceStatus.Paid,
                Total = 100,
                AmountPaid = 100
            };
            var draft = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-DF",
                Status = InvoiceStatus.Draft,
                Total = 50,
                Lines =
                {
                    new InvoiceLine { Description = "Work", Quantity = 1, UnitPrice = 50m }
                }
            };
            var emptyDraft = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-EMPTY",
                Status = InvoiceStatus.Draft,
                Total = 0
            };
            db.Set<Invoice>().AddRange(paid, draft, emptyDraft);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateStatusAsync(paid.Id, InvoiceStatus.Sent));

            var emptyEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateStatusAsync(emptyDraft.Id, InvoiceStatus.Sent));
            Assert.Contains("no lines", emptyEx.Message, StringComparison.OrdinalIgnoreCase);

            await service.UpdateStatusAsync(draft.Id, InvoiceStatus.Sent);
            var saved = await db.Set<Invoice>().FirstAsync(i => i.Id == draft.Id);
            Assert.Equal(InvoiceStatus.Sent, saved.Status);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_RejectsAmountTooHigh()
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
                InvoiceNumber = "INV-BIG",
                Status = InvoiceStatus.Sent,
                Total = 200_000_000m,
                Lines = { new InvoiceLine { Description = "Work", Quantity = 1, UnitPrice = 200_000_000m } }
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(invoice.Id, 100_000_001m, DateTime.UtcNow.Date, null, null, null));
            Assert.Contains("100,000,000", ex.Message);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_RejectsFuturePaymentDate()
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
                InvoiceNumber = "INV-FUT",
                Status = InvoiceStatus.Sent,
                Total = 100m,
                Lines = { new InvoiceLine { Description = "Work", Quantity = 1, UnitPrice = 100m } }
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date.AddDays(14), null, null, null));
            Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_ThrowsWhenInvoiceCancelled()
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
                InvoiceNumber = "INV-CANCEL-PAY",
                Status = InvoiceStatus.Cancelled,
                Subtotal = 100m,
                Tax = 0m,
                Total = 100m,
                AmountPaid = 0m
            };
            db.Set<Invoice>().Add(invoice);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(invoice.Id, 10m, DateTime.UtcNow.Date, null, null, null));
            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RecordPaymentAsync_RejectsOverpaymentAndDraft()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Pay Co" };
            db.Set<Customer>().Add(customer);
            var draft = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-DR",
                Status = InvoiceStatus.Draft,
                Total = 500
            };
            var sent = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-OV",
                Status = InvoiceStatus.Sent,
                Total = 200,
                AmountPaid = 0
            };
            db.Set<Invoice>().AddRange(draft, sent);
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(draft.Id, 10m, DateTime.UtcNow, null, null, null));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(sent.Id, 250m, DateTime.UtcNow, null, null, null));

            await service.RecordPaymentAsync(sent.Id, 200m, DateTime.UtcNow, "full", null, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RecordPaymentAsync(sent.Id, 1m, DateTime.UtcNow, null, null, null));
        }
    }
}