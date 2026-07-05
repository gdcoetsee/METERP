using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: CRM opportunity handoff into the quote spine (customer linkage, conversion, audit).
/// </summary>
public class CrmSpineFlowTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public OpportunityService Opportunities { get; }
        public QuoteService Quotes { get; }
        public AuditService Audit { get; }

        public Harness()
        {
            TenantId = Guid.NewGuid();
            var tenantProvider = new Mock<ITenantProvider>();
            tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(TenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUser.Setup(u => u.UserName).Returns("crm-spine@test");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"crm-spine-{Guid.NewGuid():N}")
                .Options;

            Db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);
            Audit = new AuditService(Db, currentUser.Object);
            Opportunities = new OpportunityService(Db, Audit);
            Quotes = new QuoteService(Db, tenantProvider: tenantProvider.Object, auditService: Audit);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task OpportunityToQuote_Handoff_LinksCustomerValueAndScope()
    {
        using var harness = new Harness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Northern Mining" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var oppId = await harness.Opportunities.CreateAsync(new Opportunity
        {
            TenantId = harness.TenantId,
            Title = "Substation upgrade",
            CustomerId = customer.Id,
            Value = 185000m,
            Stage = OpportunityStage.Qualified,
            Notes = "Remote site — include mobilization travel"
        });

        var opp = await harness.Opportunities.GetByIdAsync(oppId);
        Assert.NotNull(opp);

        var scopeText = harness.Opportunities.BuildAiScopeText(opp);
        var quoteId = await harness.Quotes.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Notes = scopeText,
            Lines =
            {
                new QuoteLine { Description = "Substation upgrade", Quantity = 1, UnitPrice = 185000m },
                new QuoteLine { Description = "Site travel", Quantity = 1, UnitPrice = 4500m, LineType = "Travel" }
            }
        });

        await harness.Opportunities.MarkConvertedToQuoteAsync(oppId, quoteId);

        var converted = await harness.Opportunities.GetByIdAsync(oppId);
        var quote = await harness.Quotes.GetByIdAsync(quoteId);

        Assert.NotNull(converted);
        Assert.NotNull(quote);
        Assert.Equal(quoteId, converted!.QuoteId);
        Assert.Equal(OpportunityStage.Proposal, converted.Stage);
        Assert.Equal(customer.Id, quote!.CustomerId);
        Assert.Contains("travel", quote.Notes!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(quote.Lines, l => l.LineType == "Travel");
    }

    [Fact]
    public async Task OpportunityQuoteToJob_Conversion_PreservesExplicitTravel()
    {
        using var harness = new Harness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "CRM Travel Co" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var oppId = await harness.Opportunities.CreateAsync(new Opportunity
        {
            TenantId = harness.TenantId,
            Title = "Panel upgrade with mobilization",
            CustomerId = customer.Id,
            Value = 62000m,
            Stage = OpportunityStage.Qualified,
            Notes = "Include explicit travel to remote yard"
        });

        var opp = await harness.Opportunities.GetByIdAsync(oppId);
        var scopeText = harness.Opportunities.BuildAiScopeText(opp!);

        var quoteId = await harness.Quotes.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Notes = scopeText,
            Lines =
            {
                new QuoteLine { Description = "Panel upgrade", Quantity = 1, UnitPrice = 50000m },
                new QuoteLine { Description = "Site travel", Quantity = 1, UnitPrice = 3200m, LineType = "Travel" }
            }
        });

        await harness.Opportunities.MarkConvertedToQuoteAsync(oppId, quoteId);

        var job = await harness.Quotes.ConvertToJobAsync(quoteId);
        var loaded = await new JobService(harness.Db).GetByIdAsync(job.Id);

        Assert.NotNull(loaded);
        Assert.Equal(quoteId, loaded!.QuoteId);
        Assert.Contains("travel", loaded.Description ?? loaded.Notes ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var travelCost = Assert.Single(loaded.ActualCosts, c => !c.IsDeleted && c.CostType == "Travel");
        Assert.Equal(3200m, travelCost.Amount);
        Assert.Contains("travel", travelCost.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpportunityQuoteToJob_Conversion_LogsQuoteConvertAudit()
    {
        using var harness = new Harness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "Audit CRM Co" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var quoteId = await harness.Quotes.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Lines =
            {
                new QuoteLine { Description = "Switchgear install", Quantity = 1, UnitPrice = 28000m },
                new QuoteLine { Description = "Mobilization travel", Quantity = 1, UnitPrice = 1800m, LineType = "Travel" }
            }
        });

        var job = await harness.Quotes.ConvertToJobAsync(quoteId);

        var entries = await harness.Audit.GetRecentAsync();
        var convertEntry = entries.First(e => e.Action == "CONVERT" && e.EntityType == "Quote");
        Assert.Contains(job.JobNumber, convertEntry.Details);
        Assert.Contains("travel", convertEntry.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpportunityToQuote_Conversion_LogsAuditEntry()
    {
        using var harness = new Harness();
        var oppId = await harness.Opportunities.CreateAsync(new Opportunity
        {
            TenantId = harness.TenantId,
            Title = "Audit handoff opp",
            CustomerName = "CRM Client",
            Value = 42000m,
            Stage = OpportunityStage.Negotiation
        });
        var quoteId = Guid.NewGuid();

        await harness.Opportunities.MarkConvertedToQuoteAsync(oppId, quoteId);

        var entries = await harness.Audit.GetRecentAsync();
        var convertEntry = entries.First(e => e.Action == "CONVERT");
        Assert.Equal("Opportunity", convertEntry.EntityType);
        Assert.Equal("Audit handoff opp", convertEntry.EntityReference);
        Assert.Contains(quoteId.ToString(), convertEntry.Details);
    }

    [Fact]
    public async Task OpportunityToQuoteToJob_EndToEnd_PreservesTravelThroughSpine()
    {
        using var harness = new Harness();
        var customer = new Customer { TenantId = harness.TenantId, Name = "End-to-end CRM" };
        harness.Db.Set<Customer>().Add(customer);
        await harness.Db.SaveChangesAsync();

        var oppId = await harness.Opportunities.CreateAsync(new Opportunity
        {
            TenantId = harness.TenantId,
            Title = "Full spine mobilization job",
            CustomerId = customer.Id,
            Value = 95000m,
            Stage = OpportunityStage.Qualified,
            Notes = "Travel to client yard required"
        });

        var opp = await harness.Opportunities.GetByIdAsync(oppId);
        var quoteId = await harness.Quotes.CreateAsync(new Quote
        {
            TenantId = harness.TenantId,
            CustomerId = customer.Id,
            TaxRate = 0.15m,
            Notes = harness.Opportunities.BuildAiScopeText(opp!),
            Lines =
            {
                new QuoteLine { Description = "Full spine work", Quantity = 1, UnitPrice = 90000m },
                new QuoteLine { Description = "Travel mobilization", Quantity = 1, UnitPrice = 5000m, LineType = "Travel" }
            }
        });

        await harness.Opportunities.MarkConvertedToQuoteAsync(oppId, quoteId);

        var convertedOpp = await harness.Opportunities.GetByIdAsync(oppId);
        var job = await harness.Quotes.ConvertToJobAsync(quoteId);
        var loadedJob = await new JobService(harness.Db).GetByIdAsync(job.Id);

        Assert.Equal(quoteId, convertedOpp!.QuoteId);
        Assert.Equal(OpportunityStage.Proposal, convertedOpp.Stage);
        Assert.NotNull(loadedJob);
        Assert.Equal(quoteId, loadedJob!.QuoteId);
        Assert.Single(loadedJob.ActualCosts, c => !c.IsDeleted && c.CostType == "Travel" && c.Amount == 5000m);
    }

    [Fact]
    public async Task OpportunityToQuote_ClosedWon_DoesNotDowngradeStageOnLink()
    {
        using var harness = new Harness();
        var oppId = await harness.Opportunities.CreateAsync(new Opportunity
        {
            TenantId = harness.TenantId,
            Title = "Won deal",
            CustomerName = "Winner Co",
            Value = 90000m,
            Stage = OpportunityStage.ClosedWon
        });

        await harness.Opportunities.MarkConvertedToQuoteAsync(oppId, Guid.NewGuid());

        var loaded = await harness.Opportunities.GetByIdAsync(oppId);
        Assert.Equal(OpportunityStage.ClosedWon, loaded!.Stage);
    }
}