using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class CustomerPaymentTermsTests
{
    [Theory]
    [InlineData(null, 30)]
    [InlineData(0, 30)]
    [InlineData(7, 7)]
    [InlineData(45, 45)]
    [InlineData(180, 180)]
    public void ResolveDays_NullOrZeroUsesThirty(int? termsDays, int expected)
    {
        Assert.Equal(expected, CustomerPaymentTerms.ResolveDays(termsDays));
    }

    [Fact]
    public void DueDateFrom_Terms7_IsInvoiceDatePlus7()
    {
        var invoiceDate = new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc);

        var due = CustomerPaymentTerms.DueDateFrom(invoiceDate, 7);

        Assert.Equal(invoiceDate.AddDays(7), due);
    }

    [Fact]
    public void DueDateFrom_MissingTerms_IsInvoiceDatePlus30()
    {
        var invoiceDate = new DateTime(2026, 10, 10, 8, 30, 0, DateTimeKind.Utc);

        Assert.Equal(invoiceDate.AddDays(30), CustomerPaymentTerms.DueDateFrom(invoiceDate, null));
        Assert.Equal(invoiceDate.AddDays(30), CustomerPaymentTerms.DueDateFrom(invoiceDate, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(181)]
    public void EnsureAllowed_RejectsTermsOutside0To180(int termsDays)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CustomerPaymentTerms.EnsureAllowed(termsDays));
        Assert.Contains("0 and 180", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(180)]
    public void EnsureAllowed_AcceptsNullZeroAnd180(int? termsDays)
    {
        CustomerPaymentTerms.EnsureAllowed(termsDays);
    }

    [Fact]
    public async Task CreateAsync_PersistsSevenDayTerms()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CustomerService(db);

        var id = await service.CreateAsync(new Customer { Name = "Seven Day Co", PaymentTermsDays = 7 });

        var saved = await db.Set<Customer>().FirstAsync(c => c.Id == id);
        Assert.Equal(7, saved.PaymentTermsDays);
    }

    [Fact]
    public async Task CreateAsync_LeavesMissingTermsNull()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CustomerService(db);

        var id = await service.CreateAsync(new Customer { Name = "Default Terms Co" });

        var saved = await db.Set<Customer>().FirstAsync(c => c.Id == id);
        Assert.Null(saved.PaymentTermsDays);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(181)]
    public async Task CreateAsync_RejectsTermsOutside0To180(int termsDays)
    {
        await using var db = CreateContext(Guid.NewGuid());
        var service = new CustomerService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Customer { Name = "Bad Terms Co", PaymentTermsDays = termsDays }));
        Assert.Contains("0 and 180", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_RejectsTermsAbove180()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new CustomerService(db);
        var id = await service.CreateAsync(new Customer { Name = "Update Terms Co", PaymentTermsDays = 30 });
        db.ChangeTracker.Clear();
        var edit = new Customer
        {
            Id = id,
            TenantId = tenantId,
            Name = "Update Terms Co",
            PaymentTermsDays = 200
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(edit));
        Assert.Contains("0 and 180", ex.Message);

        var saved = await db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == id);
        Assert.Equal(30, saved.PaymentTermsDays);
    }

    [Fact]
    public async Task CreateFromJobAsync_Terms7_DueDateIsInvoiceDatePlus7()
    {
        var (service, db, tenantId) = CreateInvoiceService();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Seven Invoice Co", PaymentTermsDays = 7 };
            var job = SignedOffJob(tenantId, customer, "Seven day job");
            db.Set<Customer>().Add(customer);
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var invoice = await service.CreateFromJobAsync(job.Id);

            Assert.Equal(invoice.InvoiceDate.AddDays(7), invoice.DueDate);
        }
    }

    [Fact]
    public async Task CreateBillingDocumentAsync_Terms45_DueDateIsInvoiceDatePlus45()
    {
        var (service, db, tenantId) = CreateInvoiceService();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Forty Five Co", PaymentTermsDays = 45 };
            var job = new Job
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                QuotedTotal = 1000m,
                Title = "Deposit terms"
            };
            db.Set<Customer>().Add(customer);
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var invoice = await service.CreateBillingDocumentAsync(job.Id, InvoiceDocumentType.Deposit);

            Assert.Equal(invoice.InvoiceDate.AddDays(45), invoice.DueDate);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task CreateFromJobAsync_NullOrZeroTerms_DueDateIsInvoiceDatePlus30(int? termsDays)
    {
        var (service, db, tenantId) = CreateInvoiceService();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = $"Default {termsDays}", PaymentTermsDays = termsDays };
            var job = SignedOffJob(tenantId, customer, "Default terms job");
            db.Set<Customer>().Add(customer);
            db.Set<Job>().Add(job);
            await db.SaveChangesAsync();

            var invoice = await service.CreateFromJobAsync(job.Id);

            Assert.Equal(invoice.InvoiceDate.AddDays(30), invoice.DueDate);
        }
    }

    [Fact]
    public async Task CreateFromJobAsync_DoesNotRewriteExistingInvoiceTotals()
    {
        var (service, db, tenantId) = CreateInvoiceService();
        await using (db)
        {
            var customer = new Customer { TenantId = tenantId, Name = "Existing Book Co" };
            var job = SignedOffJob(tenantId, customer, "New bill");
            var existing = new Invoice
            {
                TenantId = tenantId,
                CustomerId = customer.Id,
                InvoiceNumber = "INV-EXISTING",
                Status = InvoiceStatus.Sent,
                Subtotal = 200m,
                Tax = 30m,
                Total = 230m,
                InvoiceDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc)
            };
            db.Set<Customer>().Add(customer);
            db.Set<Job>().Add(job);
            db.Set<Invoice>().Add(existing);
            await db.SaveChangesAsync();

            await service.CreateFromJobAsync(job.Id);

            var saved = await db.Set<Invoice>().FirstAsync(i => i.Id == existing.Id);
            Assert.Equal(200m, saved.Subtotal);
            Assert.Equal(30m, saved.Tax);
            Assert.Equal(230m, saved.Total);
            Assert.Equal(existing.DueDate, saved.DueDate);
            Assert.Null(customer.PaymentTermsDays);
        }
    }

    private static Job SignedOffJob(Guid tenantId, Customer customer, string title) =>
        new()
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuotedTotal = 1000m,
            Title = title,
            SignOffStatus = JobSignOffStatus.SignedOff
        };

    private static AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    private static (InvoiceService Service, AppDbContext Db, Guid TenantId) CreateInvoiceService()
    {
        var tenantId = Guid.NewGuid();
        var db = CreateContext(tenantId);
        return (new InvoiceService(db), db, tenantId);
    }
}
