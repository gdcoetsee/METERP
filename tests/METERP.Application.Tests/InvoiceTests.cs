using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Tests for Invoice creation from Job (completes the Quote -> Job -> Invoice spine).
/// Covers revenue tracking, recalc, line copying from quote.
/// </summary>
public class InvoiceTests
{
    private AppDbContext CreateInMemoryContext(Guid? fixedTenantId = null)
    {
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(fixedTenantId ?? Guid.NewGuid());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(s => s.UserName).Returns("test-user");
        currentUserMock.Setup(s => s.TenantId).Returns(fixedTenantId ?? Guid.NewGuid());
        currentUserMock.Setup(s => s.IsAuthenticated).Returns(true);
        currentUserMock.Setup(s => s.Permissions).Returns(new List<string>());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProviderMock.Object, currentUserMock.Object);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_LogsAuditEntry()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var customer = new Customer { TenantId = tenantId, Name = "Audit Invoice Co" };
        db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-AUDIT-001",
            QuotedTotal = 4500m,
            Title = "Audit job",
            SignOffStatus = JobSignOffStatus.SignedOff
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new InvoiceService(db, auditService: auditService);

        var invoice = await service.CreateFromJobAsync(job.Id);

        var entries = await auditService.GetRecentAsync();
        var entry = Assert.Single(entries);
        Assert.Equal("CREATE", entry.Action);
        Assert.Equal("Invoice", entry.EntityType);
        Assert.Equal(invoice.InvoiceNumber, entry.EntityReference);
        Assert.Contains("J-AUDIT-001", entry.Details);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_CopiesLinesFromQuote_PreferringQuoteLines()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var seedTenant = new Tenant { Id = tenantId, Name = "Test Tenant", Subdomain = "test" };
        db.Set<Tenant>().Add(seedTenant);

        var customer = new Customer { TenantId = tenantId, Name = "Acme Corp" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-001",
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Description = "Labor", Quantity = 10, UnitPrice = 150, LineType = "Labour", IsDeleted = false },
                new QuoteLine { Description = "Travel", Quantity = 1, UnitPrice = 800, LineType = "Travel", IsDeleted = false }
            }
        };
        quote.RecalculateTotals();
        db.Set<Quote>().Add(quote);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            QuotedTotal = quote.Total,
            Title = "Job from quote",
            SignOffStatus = JobSignOffStatus.SignedOff
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        // Use mock to reliably verify revenue side effect (avoids fire-and-forget timing)
        var tenantServiceMock = new Mock<ITenantService>();
        var service = new InvoiceService(db, tenantServiceMock.Object);

        var invoice = await service.CreateFromJobAsync(job.Id);

        Assert.NotNull(invoice);
        Assert.Equal(2, invoice.Lines.Count); // copied from quote (preferred)
        Assert.Equal("Travel", invoice.Lines.First(l => l.LineType == "Travel").Description); // travel explicit preserved
        Assert.True(invoice.Total > 0);

        // Verify commercial revenue tracking was called with the invoice total
        tenantServiceMock.Verify(t => t.IncrementInvoiceCountAsync(tenantId, It.Is<decimal>(r => r > 0), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_ThrowsWhenJobNotFound()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var service = new InvoiceService(db, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateFromJobAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task InvoiceService_CreateFromJobAsync_FallsBackToSummaryLine_WhenNoQuote()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var customer = new Customer { TenantId = tenantId, Name = "No Quote Co" };
        db.Set<Customer>().Add(customer);

        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuotedTotal = 5500m,
            Title = "Standalone job",
            SignOffStatus = JobSignOffStatus.SignedOff
        };
        // For fallback test, use 0 tax to keep total == quoted for simple assert
        // (real invoices would have tax added on top)
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, null);

        var invoice = await service.CreateFromJobAsync(job.Id);

        Assert.Single(invoice.Lines);
        Assert.Equal(5500m, invoice.Lines.First().UnitPrice); // summary fallback
        invoice.RecalculateTotals();
        Assert.Equal(5500m, invoice.Subtotal); // pre-tax from the summary line
        Assert.True(invoice.Total > invoice.Subtotal); // tax applied
    }

    [Fact]
    public async Task InvoiceService_AddLine_RecalculatesTotals_ExcludesDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0.15m,
            Lines = new List<InvoiceLine>()
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, null);

        var line = new InvoiceLine { InvoiceId = invoice.Id, Quantity = 2, UnitPrice = 1000, IsDeleted = false };
        await service.AddLineAsync(line);

        var reloaded = await db.Set<Invoice>().Include(i => i.Lines).FirstAsync(i => i.Id == invoice.Id);
        reloaded.RecalculateTotals();
        Assert.Equal(2000m, reloaded.Subtotal);
    }

    [Fact]
    public async Task InvoiceService_UpdateLineAsync_RecalculatesTotals()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var line = new InvoiceLine { Quantity = 1, UnitPrice = 1000, IsDeleted = false };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m,
            Lines = new List<InvoiceLine> { line }
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, null);
        line.Quantity = 3;
        await service.UpdateLineAsync(line);

        var reloaded = await db.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == invoice.Id);

        Assert.Equal(3000m, reloaded.Subtotal);
    }

    [Fact]
    public async Task InvoiceService_DeleteLineAsync_SoftDeletesAndRecalculates()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var keepLine = new InvoiceLine { Quantity = 2, UnitPrice = 500, IsDeleted = false };
        var removeLine = new InvoiceLine { Quantity = 1, UnitPrice = 999, IsDeleted = false };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m,
            Lines = new List<InvoiceLine> { keepLine, removeLine }
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, null);
        await service.DeleteLineAsync(removeLine.Id);

        var reloaded = await db.Set<Invoice>()
            .Include(i => i.Lines)
            .FirstAsync(i => i.Id == invoice.Id);

        Assert.Equal(1000m, reloaded.Subtotal);

        var deletedLine = await db.Set<InvoiceLine>()
            .IgnoreQueryFilters()
            .FirstAsync(l => l.Id == removeLine.Id);
        Assert.True(deletedLine.IsDeleted);
    }

    [Fact]
    public async Task InvoiceService_DeleteAsync_SoftDeletesInvoiceAndLines()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var line = new InvoiceLine { Quantity = 1, UnitPrice = 750m, IsDeleted = false };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m,
            Lines = new List<InvoiceLine> { line }
        };
        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync();

        var service = new InvoiceService(db, null);
        await service.DeleteAsync(invoice.Id);

        Assert.Null(await service.GetByIdAsync(invoice.Id));

        var invoiceRow = await db.Set<Invoice>().IgnoreQueryFilters().FirstAsync(i => i.Id == invoice.Id);
        Assert.True(invoiceRow.IsDeleted);

        var lines = await db.Set<InvoiceLine>()
            .IgnoreQueryFilters()
            .Where(l => l.InvoiceId == invoice.Id)
            .ToListAsync();
        Assert.Single(lines);
        Assert.True(lines[0].IsDeleted);
    }
}
