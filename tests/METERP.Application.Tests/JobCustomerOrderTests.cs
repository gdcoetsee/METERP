using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class JobCustomerOrderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Blank_IsNull(string? value)
    {
        Assert.Null(JobCustomerOrder.Normalize(value));
    }

    [Fact]
    public void Normalize_Trims()
    {
        Assert.Equal("PO-44", JobCustomerOrder.Normalize("  PO-44  "));
    }

    [Fact]
    public void Normalize_RejectsOverlong()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JobCustomerOrder.Normalize(new string('A', JobCustomerOrder.MaxLength + 1)));
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public void From_PrefersStoredCustomerOrder_OverNoteToken()
    {
        var job = new Job
        {
            JobNumber = "FT1",
            Notes = "CustomerOrderNo=PO-44 | Team=FT",
            CustomerOrderNo = "PO-99"
        };

        var face = JobCardFace.From(job);

        Assert.Equal("PO-99", face.CustomerOrderNo);
        Assert.Equal("FT", face.Team);
    }

    [Fact]
    public void From_UsesNoteToken_WhenStoredCustomerOrderIsNull()
    {
        var job = new Job
        {
            JobNumber = "FT1",
            Notes = "CustomerOrderNo=PO-44",
            CustomerOrderNo = null
        };

        Assert.Equal("PO-44", JobCardFace.From(job).CustomerOrderNo);
    }

    [Fact]
    public async Task UpdateCustomerOrderNoAsync_RoundTrips_AndLeavesTotalsAlone()
    {
        var tenantId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateDb(tenantId, dbName);
        var service = new JobService(db, tenantProvider: Tenant(tenantId));

        var customer = new Customer { TenantId = tenantId, Name = "Order Co" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-PO",
            Status = QuoteStatus.Accepted,
            TaxRate = 0.15m,
            Subtotal = 1000m,
            Tax = 150m,
            Total = 1150m
        };
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            JobNumber = "J-PO",
            Title = "Install",
            QuotedTotal = 1150m,
            Status = JobStatus.Closed,
            CustomerOrderNo = null
        };
        var invoice = new Invoice
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobId = job.Id,
            InvoiceNumber = "INV-PO",
            Subtotal = 1000m,
            Tax = 150m,
            Total = 1150m,
            TaxRate = 0.15m
        };
        db.AddRange(customer, quote, job, invoice);
        await db.SaveChangesAsync();

        Assert.Null(job.CustomerOrderNo);

        await service.UpdateCustomerOrderNoAsync(job.Id, "  PO-44  ");

        var saved = await service.GetByIdAsync(job.Id);
        var savedQuote = await db.Set<Quote>().SingleAsync(q => q.Id == quote.Id);
        var savedInvoice = await db.Set<Invoice>().SingleAsync(i => i.Id == invoice.Id);

        Assert.Equal("PO-44", saved!.CustomerOrderNo);
        Assert.Equal(1150m, saved.QuotedTotal);
        Assert.Equal(1000m, savedQuote.Subtotal);
        Assert.Equal(150m, savedQuote.Tax);
        Assert.Equal(1150m, savedQuote.Total);
        Assert.Equal(1000m, savedInvoice.Subtotal);
        Assert.Equal(150m, savedInvoice.Tax);
        Assert.Equal(1150m, savedInvoice.Total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateCustomerOrderNoAsync_Blank_StaysNull(string? value)
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db, tenantProvider: Tenant(tenantId));
        var job = await SeedJob(db, tenantId, "PO-KEEP");

        await service.UpdateCustomerOrderNoAsync(job.Id, value);

        var saved = await service.GetByIdAsync(job.Id);
        Assert.Null(saved!.CustomerOrderNo);
        Assert.Equal(1000m, saved.QuotedTotal);
    }

    [Fact]
    public async Task UpdateCustomerOrderNoAsync_Overlong_DoesNotSave()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db, tenantProvider: Tenant(tenantId));
        var job = await SeedJob(db, tenantId, "PO-KEEP");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCustomerOrderNoAsync(job.Id, new string('B', 101)));

        var saved = await service.GetByIdAsync(job.Id);
        Assert.Equal("PO-KEEP", saved!.CustomerOrderNo);
        Assert.Equal(1000m, saved.QuotedTotal);
    }

    [Fact]
    public async Task UpdateCustomerOrderNoAsync_OtherTenant_IsRefused()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        await using var dbA = CreateDb(tenantA, dbName);
        var job = await SeedJob(dbA, tenantA, null);

        await using var dbB = CreateDb(tenantB, dbName);
        var other = new JobService(dbB, tenantProvider: Tenant(tenantB));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.UpdateCustomerOrderNoAsync(job.Id, "PO-LEAK"));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);

        await using var check = CreateDb(tenantA, dbName);
        var saved = await check.Set<Job>().SingleAsync(j => j.Id == job.Id);
        Assert.Null(saved.CustomerOrderNo);
    }

    [Fact]
    public async Task UpdateCustomerOrderNoAsync_SoftDeleted_IsRefused()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var service = new JobService(db, tenantProvider: Tenant(tenantId));
        var job = await SeedJob(db, tenantId, null);
        job.IsDeleted = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateCustomerOrderNoAsync(job.Id, "PO-GONE"));

        var saved = await db.Set<Job>().IgnoreQueryFilters().SingleAsync(j => j.Id == job.Id);
        Assert.Null(saved.CustomerOrderNo);
        Assert.True(saved.IsDeleted);
    }

    [Fact]
    public async Task ConvertToJobAsync_CopiesCustomerOrder_AndLeavesQuoteTotals()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var customer = new Customer { TenantId = tenantId, Name = "Quote Co" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-COPY",
            Status = QuoteStatus.Accepted,
            TaxRate = 0.15m,
            Notes = "Site work | CustomerOrderNo=PO-44 | Team=FT",
            Lines = new List<QuoteLine>
            {
                new() { Description = "Install", Quantity = 1, UnitPrice = 1000m }
            }
        };
        quote.RecalculateTotals();
        db.AddRange(customer, quote);
        await db.SaveChangesAsync();
        var subtotal = quote.Subtotal;
        var tax = quote.Tax;
        var total = quote.Total;

        var job = await new QuoteService(db, tenantProvider: Tenant(tenantId)).ConvertToJobAsync(quote.Id);

        Assert.Equal("PO-44", job.CustomerOrderNo);
        Assert.Equal(total, job.QuotedTotal);
        var savedQuote = await db.Set<Quote>().SingleAsync(q => q.Id == quote.Id);
        Assert.Equal(subtotal, savedQuote.Subtotal);
        Assert.Equal(tax, savedQuote.Tax);
        Assert.Equal(total, savedQuote.Total);
    }

    [Fact]
    public async Task ConvertToJobAsync_WithoutToken_LeavesCustomerOrderNull()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, Guid.NewGuid().ToString("N"));
        var customer = new Customer { TenantId = tenantId, Name = "Plain Co" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-PLAIN",
            Status = QuoteStatus.Sent,
            TaxRate = 0m,
            Notes = "No order number here",
            Lines = new List<QuoteLine>
            {
                new() { Description = "Labour", Quantity = 1, UnitPrice = 500m }
            }
        };
        quote.RecalculateTotals();
        db.AddRange(customer, quote);
        await db.SaveChangesAsync();

        var job = await new QuoteService(db, tenantProvider: Tenant(tenantId)).ConvertToJobAsync(quote.Id);

        Assert.Null(job.CustomerOrderNo);
        Assert.Equal(500m, job.QuotedTotal);
        var savedQuote = await db.Set<Quote>().SingleAsync(q => q.Id == quote.Id);
        Assert.Equal(500m, savedQuote.Total);
        Assert.Equal(0m, savedQuote.Tax);
    }

    private static async Task<Job> SeedJob(AppDbContext db, Guid tenantId, string? customerOrderNo)
    {
        var customer = new Customer { TenantId = tenantId, Name = "Seed Co" };
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-" + Guid.NewGuid().ToString("N")[..6],
            Title = "Seed job",
            QuotedTotal = 1000m,
            Status = JobStatus.InProgress,
            CustomerOrderNo = customerOrderNo
        };
        db.AddRange(customer, job);
        await db.SaveChangesAsync();
        return job;
    }

    private static ITenantProvider Tenant(Guid tenantId)
    {
        var tenant = new Mock<ITenantProvider>();
        tenant.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        return tenant.Object;
    }

    private static AppDbContext CreateDb(Guid tenantId, string databaseName)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserName).Returns("tester");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new AppDbContext(options, Tenant(tenantId), currentUser.Object);
    }
}
