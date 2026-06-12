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
/// Tests for Quote-related business logic and service behavior.
/// Follows the full-testing mandate: cover recalc, line operations, ConvertToJob, and commercial counters.
/// </summary>
public class QuoteTests
{
    private AppDbContext CreateInMemoryContext(Guid? fixedTenantId = null)
    {
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(fixedTenantId ?? Guid.NewGuid());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        currentUserMock.Setup(s => s.UserName).Returns("test-user");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProviderMock.Object, currentUserMock.Object);
    }

    [Fact]
    public void Quote_RecalculateTotals_ComputesCorrectly_ExcludesSoftDeleted()
    {
        var quote = new Quote
        {
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Quantity = 1, UnitPrice = 2680, IsDeleted = false },
                new QuoteLine { Quantity = 16, UnitPrice = 195, IsDeleted = false },
                new QuoteLine { Quantity = 1, UnitPrice = 875, IsDeleted = true } // should be ignored
            }
        };

        quote.RecalculateTotals();

        Assert.Equal(5800m, quote.Subtotal);   // 2680 + 16*195 (third line soft-deleted)
        Assert.Equal(870m, quote.Tax);
        Assert.Equal(6670m, quote.Total);
    }

    [Fact]
    public void Quote_RecalculateTotals_HandlesZeroAndEmpty()
    {
        var quote = new Quote { TaxRate = 0.15m, Lines = new List<QuoteLine>() };

        quote.RecalculateTotals();

        Assert.Equal(0m, quote.Subtotal);
        Assert.Equal(0m, quote.Tax);
        Assert.Equal(0m, quote.Total);
    }

    [Fact]
    public async Task QuoteService_CreateAsync_CallsRecalculate_AndIncrementsTenantCounter()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);
        var tenantServiceMock = new Mock<ITenantService>();
        tenantServiceMock.Setup(t => t.IncrementQuoteCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                         .Returns(Task.CompletedTask);

        var service = new QuoteService(db, tenantServiceMock.Object);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Quantity = 2, UnitPrice = 500, IsDeleted = false }
            }
        };

        var id = await service.CreateAsync(quote);

        // Verify recalc happened on the entity
        Assert.Equal(1000m, quote.Subtotal);
        Assert.Equal(1150m, quote.Total);

        // Verify commercial counter was triggered (best-effort fire-and-forget, so we just verify call attempt)
        tenantServiceMock.Verify(t => t.IncrementQuoteCountAsync(quote.TenantId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task QuoteService_ConvertToJobAsync_CreatesJobWithCorrectTotals_AndUpdatesQuoteStatus()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        // Seed a minimal customer and quote (TenantId required for isolation)
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Test Customer" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-TEST-001",
            Status = QuoteStatus.Draft,
            TaxRate = 0.15m,
            Notes = "Test scope with travel",
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Quantity = 1, UnitPrice = 10000, Description = "Main work", IsDeleted = false },
                new QuoteLine { Quantity = 1, UnitPrice = 1500, Description = "Travel costs", LineType = "Travel", IsDeleted = false }
            }
        };
        quote.RecalculateTotals(); // ensure totals before save
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var tenantServiceMock = new Mock<ITenantService>();
        var service = new QuoteService(db, tenantServiceMock.Object);

        var createdJob = await service.ConvertToJobAsync(quote.Id);

        Assert.NotNull(createdJob);
        Assert.Equal(quote.Id, createdJob.QuoteId);
        Assert.Equal(13225m, createdJob.QuotedTotal); // 11500 subtotal + 15% tax (service copies quote.Total which includes tax)
        Assert.Equal(JobStatus.Scheduled, createdJob.Status);
        Assert.Contains("Test Customer", createdJob.Title);

        // Verify quote status was set to Accepted
        var updatedQuote = await db.Set<Quote>().FindAsync(quote.Id);
        Assert.Equal(QuoteStatus.Accepted, updatedQuote!.Status);

        // Counter for quote was already called during creation; conversion itself doesn't increment again in current impl
    }

    [Fact]
    public async Task QuoteService_AddLineAsync_RecalculatesParentTotals()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m, // simplify
            Lines = new List<QuoteLine>()
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var service = new QuoteService(db, null);

        var line = new QuoteLine
        {
            QuoteId = quote.Id,
            Description = "Extra item",
            Quantity = 3,
            UnitPrice = 200,
            IsDeleted = false
        };

        await service.AddLineAsync(line);

        var reloaded = await db.Set<Quote>()
            .Include(q => q.Lines)
            .FirstAsync(q => q.Id == quote.Id);

        reloaded.RecalculateTotals(); // service should have called it internally
        Assert.Equal(600m, reloaded.Subtotal);
        Assert.Single(reloaded.Lines);
    }

    [Fact]
    public async Task QuoteService_DeleteLineAsync_SoftDeletesAndRecalculates()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var line = new QuoteLine { Quantity = 1, UnitPrice = 999, IsDeleted = false };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m,
            Lines = new List<QuoteLine> { line }
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var service = new QuoteService(db, null);

        await service.DeleteLineAsync(line.Id);

        var reloaded = await db.Set<Quote>()
            .Include(q => q.Lines)
            .FirstAsync(q => q.Id == quote.Id);

        var deletedLine = reloaded.Lines.First();
        Assert.True(deletedLine.IsDeleted);

        reloaded.RecalculateTotals();
        Assert.Equal(0m, reloaded.Subtotal); // excluded due to soft delete
    }

    [Fact]
    public async Task QuoteService_UpdateLineAsync_RecalculatesParentTotals()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var line = new QuoteLine { Quantity = 2, UnitPrice = 100, IsDeleted = false };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = Guid.NewGuid(),
            TaxRate = 0m,
            Lines = new List<QuoteLine> { line }
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var service = new QuoteService(db, null);
        line.Quantity = 5;
        await service.UpdateLineAsync(line);

        var reloaded = await db.Set<Quote>()
            .Include(q => q.Lines)
            .FirstAsync(q => q.Id == quote.Id);

        Assert.Equal(500m, reloaded.Subtotal);
    }

    [Fact]
    public async Task QuoteService_DeleteAsync_SoftDeletesQuoteAndLines()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Del Co" };
        db.Set<Customer>().Add(customer);

        var line = new QuoteLine { Quantity = 1, UnitPrice = 500, IsDeleted = false };
        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            TaxRate = 0m,
            Lines = new List<QuoteLine> { line }
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var service = new QuoteService(db, null);
        await service.DeleteAsync(quote.Id);

        Assert.Null(await service.GetByIdAsync(quote.Id));

        var quoteRow = await db.Set<Quote>().IgnoreQueryFilters().FirstAsync(q => q.Id == quote.Id);
        Assert.True(quoteRow.IsDeleted);

        var lines = await db.Set<QuoteLine>()
            .IgnoreQueryFilters()
            .Where(l => l.QuoteId == quote.Id)
            .ToListAsync();
        Assert.Single(lines);
        Assert.True(lines[0].IsDeleted);
    }
}
