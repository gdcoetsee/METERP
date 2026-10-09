using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AllocateReceiptTests
{
    [Fact]
    public async Task AllocateReceiptAsync_SplitsOneBankAmount_UpdatesBalancesStatementAndAudit()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb($"alloc-ok-{Guid.NewGuid():N}", tenantId);
        var audit = new Mock<IAuditService>();
        var notifications = new Mock<ITenantNotificationService>();
        notifications.Setup(n => n.CreateAsync(It.IsAny<TenantNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new InvoiceService(db, auditService: audit.Object, notifications: notifications.Object);

        var customer = AddCustomer(db, "Alloc Co");
        var first = AddInvoice(db, customer, "INV-A", 1000m);
        var second = AddInvoice(db, customer, "INV-B", 500m, dueDate: DateTime.UtcNow.Date.AddDays(-3));
        second.Status = InvoiceStatus.Overdue;
        await db.SaveChangesAsync();

        var before = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(before);
        Assert.Equal(1500m, before.ClosingBalance);

        var ids = await service.AllocateReceiptAsync(
            customer.Id,
            600m,
            DateTime.UtcNow.Date,
            "EFT-9",
            new[]
            {
                new ReceiptAllocationLine(first.Id, 400m),
                new ReceiptAllocationLine(second.Id, 200m)
            },
            Guid.NewGuid(),
            "split");

        Assert.Equal(2, ids.Count);
        var savedA = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == first.Id);
        var savedB = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == second.Id);
        Assert.Equal(400m, savedA.AmountPaid);
        Assert.Equal(InvoiceStatus.PartiallyPaid, savedA.Status);
        Assert.Equal(200m, savedB.AmountPaid);
        Assert.Equal(InvoiceStatus.PartiallyPaid, savedB.Status);

        var payments = await db.Set<InvoicePayment>().AsNoTracking().OrderBy(p => p.Amount).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.All(payments, p =>
        {
            Assert.Equal("EFT-9", p.Reference);
            Assert.Equal("split", p.Notes);
            Assert.Equal(DateTime.UtcNow.Date, p.PaymentDate);
        });
        Assert.Equal(600m, payments.Sum(p => p.Amount));

        var open = await service.GetAllocatableInvoicesAsync(customer.Id);
        Assert.Equal(new[] { "INV-A", "INV-B" }, open.Select(r => r.InvoiceNumber).ToArray());
        Assert.Equal(600m, open.Single(r => r.InvoiceNumber == "INV-A").BalanceDue);
        Assert.Equal(300m, open.Single(r => r.InvoiceNumber == "INV-B").BalanceDue);

        var mid = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(mid);
        Assert.Equal(900m, mid.ClosingBalance);
        Assert.Contains(mid.Lines, l => l.Kind == "Receipt" && l.Reference == "EFT-9" && l.Credit == 400m);
        Assert.Contains(mid.Lines, l => l.Kind == "Receipt" && l.Reference == "EFT-9" && l.Credit == 200m);

        var tooMuch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AllocateReceiptAsync(
                customer.Id,
                1000m,
                DateTime.UtcNow.Date,
                "EFT-10",
                new[]
                {
                    new ReceiptAllocationLine(first.Id, 600m),
                    new ReceiptAllocationLine(second.Id, 400m)
                },
                null,
                null));
        Assert.Contains("exceeds balance", tooMuch.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(400m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == first.Id)).AmountPaid);
        Assert.Equal(200m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == second.Id)).AmountPaid);

        await service.AllocateReceiptAsync(
            customer.Id,
            900m,
            DateTime.UtcNow.Date,
            "EFT-11",
            new[]
            {
                new ReceiptAllocationLine(first.Id, 600m),
                new ReceiptAllocationLine(second.Id, 300m)
            },
            null,
            null);

        var closed = await service.GetCustomerStatementAsync(customer.Id);
        Assert.NotNull(closed);
        Assert.Equal(0m, closed.ClosingBalance);
        Assert.Empty(await service.GetAllocatableInvoicesAsync(customer.Id));

        audit.Verify(a => a.LogAsync(
            "ALLOCATE",
            "Invoice",
            "EFT-9",
            It.Is<string>(d => d.Contains("EFT-9") && d.Contains("INV-A") && d.Contains("INV-B")),
            It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.CreateAsync(
            It.Is<TenantNotification>(t =>
                t.Category == "collections"
                && t.Message.Contains("INV-A")
                && t.Message.Contains("INV-B")
                && t.Title.Contains("EFT-9")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllocateReceiptAsync_RefusesWhenTheLinesDoNotSumToTheBankAmount()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Sum Co");
            var first = AddInvoice(db, customer, "INV-1", 400m);
            var second = AddInvoice(db, customer, "INV-2", 400m);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id,
                    500m,
                    DateTime.UtcNow.Date,
                    "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(first.Id, 200m),
                        new ReceiptAllocationLine(second.Id, 200m)
                    },
                    null,
                    null));
            Assert.Contains("do not add up", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await db.Set<InvoicePayment>().ToListAsync());
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == first.Id)).AmountPaid);
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == second.Id)).AmountPaid);
        }
    }

    [Theory]
    [InlineData(InvoiceDocumentType.Standard, InvoiceStatus.Draft, "Send")]
    [InlineData(InvoiceDocumentType.Proforma, InvoiceStatus.Sent, "proforma")]
    [InlineData(InvoiceDocumentType.Standard, InvoiceStatus.Cancelled, "cancelled")]
    [InlineData(InvoiceDocumentType.CreditNote, InvoiceStatus.Sent, "credit note")]
    public async Task AllocateReceiptAsync_RefusesUnpayableDocuments(
        InvoiceDocumentType type,
        InvoiceStatus status,
        string messagePart)
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Refuse Co");
            var good = AddInvoice(db, customer, "INV-GOOD", 200m);
            var bad = AddInvoice(db, customer, "INV-BAD", 115m, status, type);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id,
                    315m,
                    DateTime.UtcNow.Date,
                    "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(good.Id, 200m),
                        new ReceiptAllocationLine(bad.Id, 115m)
                    },
                    null,
                    null));
            Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("INV-BAD", ex.Message);
            Assert.Empty(await db.Set<InvoicePayment>().ToListAsync());
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == good.Id)).AmountPaid);
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == bad.Id)).AmountPaid);
        }
    }

    [Fact]
    public async Task AllocateReceiptAsync_RefusesAnotherTenant_AndLeavesBothInvoices()
    {
        var database = $"alloc-tenant-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid localId;
        Guid foreignId;
        Guid customerAId;

        await using (var dbA = CreateDb(database, tenantA))
        {
            var customerA = AddCustomer(dbA, "Local");
            var local = AddInvoice(dbA, customerA, "INV-LOCAL", 250m);
            await dbA.SaveChangesAsync();
            localId = local.Id;
            customerAId = customerA.Id;
        }

        await using (var dbB = CreateDb(database, tenantB))
        {
            var customerB = AddCustomer(dbB, "Foreign");
            var foreign = AddInvoice(dbB, customerB, "INV-FOREIGN", 250m);
            await dbB.SaveChangesAsync();
            foreignId = foreign.Id;
        }

        await using var again = CreateDb(database, tenantA);
        var serviceA = new InvoiceService(again);
        var open = await serviceA.GetAllocatableInvoicesAsync(customerAId);
        Assert.Equal(new[] { "INV-LOCAL" }, open.Select(r => r.InvoiceNumber).ToArray());

        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            serviceA.AllocateReceiptAsync(
                customerAId,
                500m,
                DateTime.UtcNow.Date,
                "EFT-X",
                new[]
                {
                    new ReceiptAllocationLine(localId, 250m),
                    new ReceiptAllocationLine(foreignId, 250m)
                },
                null,
                null));
        Assert.Equal("Cannot allocate a receipt to an invoice on another tenant.", denied.Message);

        await using var checkA = CreateDb(database, tenantA);
        await using var checkB = CreateDb(database, tenantB);
        Assert.Equal(0m, (await checkA.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == localId)).AmountPaid);
        Assert.Equal(0m, (await checkB.Set<Invoice>().IgnoreQueryFilters().SingleAsync(i => i.Id == foreignId)).AmountPaid);
        Assert.Empty(await checkA.Set<InvoicePayment>().IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AllocateReceiptAsync_RefusesInvoicesForADifferentCustomer()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var firstCustomer = AddCustomer(db, "One");
            var secondCustomer = AddCustomer(db, "Two");
            var first = AddInvoice(db, firstCustomer, "INV-ONE", 100m);
            var second = AddInvoice(db, secondCustomer, "INV-TWO", 100m);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    firstCustomer.Id,
                    200m,
                    DateTime.UtcNow.Date,
                    "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(first.Id, 100m),
                        new ReceiptAllocationLine(second.Id, 100m)
                    },
                    null,
                    null));
            Assert.Contains("same customer", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await db.Set<InvoicePayment>().ToListAsync());
        }
    }

    [Fact]
    public async Task AllocateReceiptAsync_RefusesASoftDeletedInvoice()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Gone");
            var live = AddInvoice(db, customer, "INV-LIVE", 80m);
            var deleted = AddInvoice(db, customer, "INV-GONE", 80m);
            deleted.IsDeleted = true;
            await db.SaveChangesAsync();

            var open = await service.GetAllocatableInvoicesAsync(customer.Id);
            Assert.Equal(new[] { "INV-LIVE" }, open.Select(r => r.InvoiceNumber).ToArray());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id,
                    160m,
                    DateTime.UtcNow.Date,
                    "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(live.Id, 80m),
                        new ReceiptAllocationLine(deleted.Id, 80m)
                    },
                    null,
                    null));
            Assert.Equal("Invoice not found.", ex.Message);
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == live.Id)).AmountPaid);
            Assert.Empty(await db.Set<InvoicePayment>().ToListAsync());
        }
    }

    [Fact]
    public async Task AllocateReceiptAsync_RequiresTwoInvoices_AReference_AndASaneDate()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Header");
            var invoice = AddInvoice(db, customer, "INV-H", 100m);
            var other = AddInvoice(db, customer, "INV-H2", 100m);
            await db.SaveChangesAsync();

            var one = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id, 100m, DateTime.UtcNow.Date, "EFT",
                    new[] { new ReceiptAllocationLine(invoice.Id, 100m) }, null, null));
            Assert.Contains("at least two", one.Message, StringComparison.OrdinalIgnoreCase);

            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id, 100m, DateTime.UtcNow.Date, "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(invoice.Id, 40m),
                        new ReceiptAllocationLine(invoice.Id, 60m)
                    },
                    null, null));
            Assert.Contains("only once", duplicate.Message, StringComparison.OrdinalIgnoreCase);

            var missingRef = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id, 150m, DateTime.UtcNow.Date, "  ",
                    new[]
                    {
                        new ReceiptAllocationLine(invoice.Id, 50m),
                        new ReceiptAllocationLine(other.Id, 100m)
                    },
                    null, null));
            Assert.Contains("reference", missingRef.Message, StringComparison.OrdinalIgnoreCase);

            var future = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AllocateReceiptAsync(
                    customer.Id, 150m, DateTime.UtcNow.Date.AddDays(14), "EFT",
                    new[]
                    {
                        new ReceiptAllocationLine(invoice.Id, 50m),
                        new ReceiptAllocationLine(other.Id, 100m)
                    },
                    null, null));
            Assert.Contains("future", future.Message, StringComparison.OrdinalIgnoreCase);

            Assert.Empty(await db.Set<InvoicePayment>().ToListAsync());
            Assert.Equal(0m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == invoice.Id)).AmountPaid);
        }
    }

    [Fact]
    public async Task AllocateReceiptAsync_SetsDepositReceivedOnlyWhenTheDepositIsFullyCovered()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Deposit Co");
            var job = new Job
            {
                CustomerId = customer.Id,
                JobNumber = "J-ALLOC",
                Title = "Transformer",
                QuotedTotal = 10000m,
                DepositPercent = 30m,
                DepositReceived = false,
                Status = JobStatus.InProgress
            };
            db.Set<Job>().Add(job);
            var deposit = AddInvoice(db, customer, "DEP-1", 1000m, type: InvoiceDocumentType.Deposit);
            deposit.JobId = job.Id;
            var progress = AddInvoice(db, customer, "INV-P", 800m);
            await db.SaveChangesAsync();

            await service.AllocateReceiptAsync(
                customer.Id,
                600m,
                DateTime.UtcNow.Date,
                "EFT-PART",
                new[]
                {
                    new ReceiptAllocationLine(deposit.Id, 400m),
                    new ReceiptAllocationLine(progress.Id, 200m)
                },
                null,
                null);

            Assert.False((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);
            Assert.Equal(InvoiceStatus.PartiallyPaid,
                (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == deposit.Id)).Status);

            await service.AllocateReceiptAsync(
                customer.Id,
                1200m,
                DateTime.UtcNow.Date,
                "EFT-FULL",
                new[]
                {
                    new ReceiptAllocationLine(deposit.Id, 600m),
                    new ReceiptAllocationLine(progress.Id, 600m)
                },
                null,
                null);

            Assert.True((await db.Set<Job>().AsNoTracking().SingleAsync(j => j.Id == job.Id)).DepositReceived);
            Assert.Equal(InvoiceStatus.Paid,
                (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == deposit.Id)).Status);
            Assert.Equal(InvoiceStatus.Paid,
                (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == progress.Id)).Status);
        }
    }

    [Fact]
    public async Task AllocateReceiptAsync_KeepsTheReceipt_WhenTheReceiptEmailThrows()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb($"alloc-mail-{Guid.NewGuid():N}", tenantId);
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));
        var service = new InvoiceService(db, email: email.Object);

        var customer = AddCustomer(db, "Mail Co");
        customer.Email = "ap@alloc.test";
        var first = AddInvoice(db, customer, "INV-M1", 100m);
        var second = AddInvoice(db, customer, "INV-M2", 50m);
        await db.SaveChangesAsync();

        var ids = await service.AllocateReceiptAsync(
            customer.Id,
            150m,
            DateTime.UtcNow.Date,
            "EFT-MAIL",
            new[]
            {
                new ReceiptAllocationLine(first.Id, 100m),
                new ReceiptAllocationLine(second.Id, 50m)
            },
            null,
            null);

        Assert.Equal(2, ids.Count);
        Assert.Equal(100m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == first.Id)).AmountPaid);
        Assert.Equal(50m, (await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == second.Id)).AmountPaid);
        email.Verify(e => e.SendEmailAsync(
            "ap@alloc.test",
            It.Is<string>(s => s.Contains("INV-M1")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllocatableInvoicesAsync_ListsOpenSalesInvoicesInDateOrder()
    {
        var (service, db, _) = Create();
        await using (db)
        {
            var customer = AddCustomer(db, "Open Co");
            var later = AddInvoice(db, customer, "INV-LATER", 10m);
            later.InvoiceDate = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
            var second = AddInvoice(db, customer, "INV-B", 30m);
            second.InvoiceDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            second.AmountPaid = 10m;
            var first = AddInvoice(db, customer, "INV-A", 20m);
            first.InvoiceDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            AddInvoice(db, customer, "INV-DRAFT", 10m, InvoiceStatus.Draft);
            AddInvoice(db, customer, "INV-PRO", 10m, InvoiceStatus.Sent, InvoiceDocumentType.Proforma);
            AddInvoice(db, customer, "INV-CN", 10m, InvoiceStatus.Sent, InvoiceDocumentType.CreditNote);
            AddInvoice(db, customer, "INV-CAN", 10m, InvoiceStatus.Cancelled);
            var paid = AddInvoice(db, customer, "INV-PAID", 10m);
            paid.AmountPaid = 10m;
            paid.Status = InvoiceStatus.Paid;
            var removed = AddInvoice(db, customer, "INV-DEL", 10m);
            removed.IsDeleted = true;
            await db.SaveChangesAsync();

            var rows = await service.GetAllocatableInvoicesAsync(customer.Id);
            Assert.Equal(new[] { "INV-A", "INV-B", "INV-LATER" }, rows.Select(r => r.InvoiceNumber).ToArray());
            Assert.Equal(20m, rows[0].BalanceDue);
            Assert.Equal(20m, rows[1].BalanceDue);
            Assert.Empty(await service.GetAllocatableInvoicesAsync(Guid.Empty));
        }
    }

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateDb($"alloc-{Guid.NewGuid():N}", tenantId);
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

    private static Customer AddCustomer(AppDbContext db, string name)
    {
        var customer = new Customer { Name = name };
        db.Set<Customer>().Add(customer);
        return customer;
    }

    private static Invoice AddInvoice(
        AppDbContext db,
        Customer customer,
        string number,
        decimal total,
        InvoiceStatus status = InvoiceStatus.Sent,
        InvoiceDocumentType type = InvoiceDocumentType.Standard,
        DateTime? dueDate = null)
    {
        var invoice = new Invoice
        {
            CustomerId = customer.Id,
            InvoiceNumber = number,
            Status = status,
            DocumentType = type,
            InvoiceDate = DateTime.UtcNow.Date,
            DueDate = dueDate ?? DateTime.UtcNow.Date.AddDays(30),
            Total = total
        };
        db.Set<Invoice>().Add(invoice);
        return invoice;
    }
}
