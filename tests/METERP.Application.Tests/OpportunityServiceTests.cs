using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class OpportunityServiceTests
{
    private AppDbContext CreateContext(Guid tenantId)
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

    [Fact]
    public async Task CreateAsync_PersistsOpportunity_WithCustomerNameFromLinkedCustomer()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme Mining" });
        await db.SaveChangesAsync();

        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Transformer upgrade",
            CustomerId = customerId,
            Value = 125000m,
            Stage = OpportunityStage.Proposal
        });

        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Acme Mining", loaded.CustomerName);
        Assert.Equal(125000m, loaded.Value);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        await service.CreateAsync(new Opportunity { Title = "Mine substation", CustomerName = "Mining Co", Value = 50000m });
        await service.CreateAsync(new Opportunity { Title = "Office lighting", CustomerName = "Retail", Value = 12000m });

        var results = await service.GetAllAsync("substation");

        Assert.Single(results);
        Assert.Equal("Mine substation", results[0].Title);
    }

    [Fact]
    public async Task AdvanceStageAsync_MovesToNextStage()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Lead opp",
            CustomerName = "Test",
            Value = 1000m,
            Stage = OpportunityStage.Lead
        });

        await service.AdvanceStageAsync(id);

        var loaded = await service.GetByIdAsync(id);
        Assert.Equal(OpportunityStage.Qualified, loaded!.Stage);
    }

    [Fact]
    public void BuildAiScopeText_IncludesTravelHint()
    {
        var service = new OpportunityService(CreateContext(Guid.NewGuid()));
        var text = service.BuildAiScopeText(new Opportunity
        {
            Title = "11kV install",
            CustomerName = "Gauteng Power",
            Value = 210000m,
            ExpectedClose = new DateTime(2026, 7, 1)
        });

        Assert.Contains("11kV install", text);
        Assert.Contains("travel", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_LinksQuoteAndAdvancesStage()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "CRM deal",
            CustomerName = "Mining Co",
            Value = 80000m,
            Stage = OpportunityStage.Qualified
        });
        var quoteId = Guid.NewGuid();

        await service.MarkConvertedToQuoteAsync(oppId, quoteId);

        var loaded = await service.GetByIdAsync(oppId);
        Assert.NotNull(loaded);
        Assert.Equal(quoteId, loaded!.QuoteId);
        Assert.Equal(OpportunityStage.Proposal, loaded.Stage);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesOpportunity()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity { Title = "Remove me", CustomerName = "X", Value = 1m });

        await service.DeleteAsync(id);

        Assert.Null(await service.GetByIdAsync(id));
        var deleted = await db.Set<Opportunity>().IgnoreQueryFilters().FirstAsync(o => o.Id == id);
        Assert.True(deleted.IsDeleted);
    }
}