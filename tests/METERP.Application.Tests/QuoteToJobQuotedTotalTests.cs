using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// P0-12: a new quote → job copies the VAT-inclusive total, not the ex-VAT subtotal.
/// </summary>
public class QuoteToJobQuotedTotalTests
{
    [Fact]
    public async Task ConvertToJob_StaleExVatHeader_SnapshotsVatInclusiveTotalOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Vat Client" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-VAT-ONCE",
            Status = QuoteStatus.Sent,
            TaxRate = 0.15m,
            Subtotal = 150m,
            Tax = 0m,
            Total = 150m,
            Lines =
            {
                new QuoteLine { Description = "Panel", Quantity = 1, UnitPrice = 100m, LineType = "Material" },
                new QuoteLine { Description = "Site travel", Quantity = 1, UnitPrice = 50m, LineType = "Travel" },
                new QuoteLine { Description = "Removed extra", Quantity = 1, UnitPrice = 999m, LineType = "Material", IsDeleted = true }
            }
        };
        db.Set<Quote>().Add(quote);

        var historical = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            JobNumber = "J-HIST",
            Title = "Already on the book",
            QuotedTotal = 80m,
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(historical);
        await db.SaveChangesAsync();

        var service = new QuoteService(db);
        var job = await service.ConvertToJobAsync(quote.Id);

        Assert.Equal(172.50m, job.QuotedTotal);
        Assert.NotEqual(150m, job.QuotedTotal);
        Assert.Equal(172.50m, InvoiceBillingCalculator.CalculateUnbilledResidual(job.QuotedTotal, 0m));

        var travel = Assert.Single(job.ActualCosts, c => !c.IsDeleted);
        Assert.Equal("Travel", travel.CostType);
        Assert.Equal(50m, travel.Amount);
        Assert.Equal(172.50m, job.QuotedTotal);

        var savedQuote = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
        Assert.Equal(150m, savedQuote.Subtotal);
        Assert.Equal(22.50m, savedQuote.Tax);
        Assert.Equal(172.50m, savedQuote.Total);
        Assert.Equal(QuoteStatus.Accepted, savedQuote.Status);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertToJobAsync(quote.Id));
        Assert.Contains("already been converted", again.Message, StringComparison.OrdinalIgnoreCase);

        var jobsForQuote = await db.Set<Job>().Where(j => j.QuoteId == quote.Id).ToListAsync();
        var only = Assert.Single(jobsForQuote);
        Assert.Equal(172.50m, only.QuotedTotal);

        var untouched = await db.Set<Job>().FirstAsync(j => j.Id == historical.Id);
        Assert.Equal(80m, untouched.QuotedTotal);
    }

    [Fact]
    public async Task ConvertToJob_AlreadyConverted_LeavesHistoricalQuotedTotal()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Old Book" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-OLD",
            Status = QuoteStatus.Accepted,
            TaxRate = 0.15m,
            Subtotal = 100m,
            Tax = 0m,
            Total = 100m,
            Lines = { new QuoteLine { Description = "Work", Quantity = 1, UnitPrice = 100m } }
        };
        var existing = new Job
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteId = quote.Id,
            JobNumber = "J-OLD",
            Title = "Imported",
            QuotedTotal = 100m
        };
        db.Set<Customer>().Add(customer);
        db.Set<Quote>().Add(quote);
        db.Set<Job>().Add(existing);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QuoteService(db).ConvertToJobAsync(quote.Id));
        Assert.Contains("already been converted", ex.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = await db.Set<Job>().FirstAsync(j => j.Id == existing.Id);
        Assert.Equal(100m, reloaded.QuotedTotal);
        var quoteRow = await db.Set<Quote>().FirstAsync(q => q.Id == quote.Id);
        Assert.Equal(100m, quoteRow.Total);
        Assert.Equal(0m, quoteRow.Tax);
    }

    [Fact]
    public async Task ConvertToJob_ZeroTaxRate_DoesNotAddVat()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "Zero Rated" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-ZERO",
            TaxRate = 0m,
            Total = 0m,
            Lines =
            {
                new QuoteLine { Description = "Supply", Quantity = 1, UnitPrice = 100m },
                new QuoteLine { Description = "Travel", Quantity = 1, UnitPrice = 20m, LineType = "Travel" }
            }
        };
        db.Set<Customer>().Add(customer);
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var job = await new QuoteService(db).ConvertToJobAsync(quote.Id);

        Assert.Equal(120m, job.QuotedTotal);
        Assert.Equal(20m, Assert.Single(job.ActualCosts).Amount);
    }

    [Fact]
    public async Task SalesOrderConvert_StaleExVatHeader_SnapshotsVatInclusiveTotalOnce()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var customer = new Customer { TenantId = tenantId, Name = "SO Client" };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-SO-VAT",
            TaxRate = 0.15m,
            Lines = { new QuoteLine { Description = "Linked", Quantity = 1, UnitPrice = 1m } }
        };
        quote.RecalculateTotals();
        db.Set<Customer>().Add(customer);
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var jobs = new JobService(db);
        var orders = new SalesOrderService(db, jobs);
        var soId = await orders.CreateAsync(new SalesOrder
        {
            QuoteId = quote.Id,
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            TaxRate = 0.15m,
            Lines =
            {
                new SalesOrderLine { Description = "Panel", Quantity = 1, UnitPrice = 100m },
                new SalesOrderLine { Description = "Site travel", Quantity = 1, UnitPrice = 50m, LineType = "Travel" }
            }
        });

        var so = await db.Set<SalesOrder>().FirstAsync(s => s.Id == soId);
        so.Tax = 0m;
        so.Total = so.Subtotal;
        await db.SaveChangesAsync();

        var job = await orders.ConvertToJobAsync(soId);

        Assert.Equal(172.50m, job.QuotedTotal);
        Assert.Equal(quote.Id, job.QuoteId);
        var travel = Assert.Single(await db.Set<JobCost>().Where(c => c.JobId == job.Id && !c.IsDeleted).ToListAsync());
        Assert.Equal("Travel", travel.CostType);
        Assert.Equal(50m, travel.Amount);

        var saved = await db.Set<SalesOrder>().FirstAsync(s => s.Id == soId);
        Assert.Equal(150m, saved.Subtotal);
        Assert.Equal(22.50m, saved.Tax);
        Assert.Equal(172.50m, saved.Total);

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => orders.ConvertToJobAsync(soId));
        Assert.Contains("already converted", again.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(172.50m, (await db.Set<Job>().FirstAsync(j => j.Id == job.Id)).QuotedTotal);
    }

    private static AppDbContext CreateContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.UserName).Returns("p0-12");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }
}
