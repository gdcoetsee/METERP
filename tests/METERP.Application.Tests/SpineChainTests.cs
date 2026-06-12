using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// End-to-end unit test for the core contractor spine: Quote → Job → Invoice with explicit travel.
/// </summary>
public class SpineChainTests
{
    private AppDbContext CreateInMemoryContext(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.UserName).Returns("spine-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task QuoteToJobToInvoice_ChainPreservesTravelAndTotals()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateInMemoryContext(tenantId);

        db.Set<Tenant>().Add(new Tenant { Id = tenantId, Name = "Spine Co", Subdomain = "spine" });
        var customer = new Customer { TenantId = tenantId, Name = "Field Client" };
        db.Set<Customer>().Add(customer);

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = customer.Id,
            QuoteNumber = "Q-SPINE-001",
            Status = QuoteStatus.Draft,
            TaxRate = 0.15m,
            Lines = new List<QuoteLine>
            {
                new QuoteLine { Description = "Panel install", Quantity = 1, UnitPrice = 10000m, IsDeleted = false },
                new QuoteLine { Description = "Site travel", Quantity = 1, UnitPrice = 1500m, LineType = "Travel", IsDeleted = false }
            }
        };
        quote.RecalculateTotals();
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();

        var quoteService = new QuoteService(db);
        var job = await quoteService.ConvertToJobAsync(quote.Id);

        Assert.Equal(quote.Id, job.QuoteId);
        Assert.Equal(13225m, job.QuotedTotal);

        var travelCost = job.ActualCosts.Single(c => c.CostType == "Travel");
        Assert.Equal(1500m, travelCost.Amount);

        var invoiceService = new InvoiceService(db);
        var invoice = await invoiceService.CreateFromJobAsync(job.Id);

        Assert.Equal(2, invoice.Lines.Count(l => !l.IsDeleted));
        Assert.Contains(invoice.Lines, l => l.LineType == "Travel" && l.UnitPrice == 1500m);
        Assert.Equal(quote.Total, invoice.Total);
        Assert.Equal(quote.Subtotal, invoice.Subtotal);
    }
}