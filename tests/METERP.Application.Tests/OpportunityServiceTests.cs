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
    public async Task AdvanceStageAsync_LogsAuditEntry()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new OpportunityService(db, auditService);

        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Audit pipeline opp",
            CustomerName = "Test",
            Value = 3000m,
            Stage = OpportunityStage.Lead
        });

        await service.AdvanceStageAsync(id);

        var entries = await auditService.GetRecentAsync();
        var advanceEntry = entries.First(e => e.Action == "UPDATE" && e.Details.Contains("Advanced"));
        Assert.Equal("Opportunity", advanceEntry.EntityType);
        Assert.Equal("Audit pipeline opp", advanceEntry.EntityReference);
    }

    [Fact]
    public async Task AdvanceStageAsync_DoesNotAdvanceFromClosedLost()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Lost deal",
            CustomerName = "Test",
            Value = 500m,
            Stage = OpportunityStage.ClosedLost
        });

        await service.AdvanceStageAsync(id);

        var loaded = await service.GetByIdAsync(id);
        Assert.Equal(OpportunityStage.ClosedLost, loaded!.Stage);
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
    public async Task MarkConvertedToQuoteAsync_LogsAuditEntry()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new OpportunityService(db, auditService);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "Convert audit opp",
            CustomerName = "Audit Co",
            Value = 42000m,
            Stage = OpportunityStage.Qualified
        });
        var quoteId = Guid.NewGuid();

        await service.MarkConvertedToQuoteAsync(oppId, quoteId);

        var convertEntry = (await auditService.GetRecentAsync()).First(e => e.Action == "CONVERT");
        Assert.Equal("Opportunity", convertEntry.EntityType);
        Assert.Equal("Convert audit opp", convertEntry.EntityReference);
        Assert.Contains(quoteId.ToString(), convertEntry.Details);
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_DoesNotChangeClosedWonStage()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "Won deal",
            CustomerName = "Winner",
            Value = 99000m,
            Stage = OpportunityStage.ClosedWon
        });
        var quoteId = Guid.NewGuid();

        await service.MarkConvertedToQuoteAsync(oppId, quoteId);

        var loaded = await service.GetByIdAsync(oppId);
        Assert.NotNull(loaded);
        Assert.Equal(quoteId, loaded!.QuoteId);
        Assert.Equal(OpportunityStage.ClosedWon, loaded.Stage);
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
    public async Task UpdateAsync_PersistsChanges()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Before update",
            CustomerName = "Client",
            Value = 4000m,
            Stage = OpportunityStage.Lead
        });

        var opp = await service.GetByIdAsync(id);
        Assert.NotNull(opp);
        opp!.Title = "After update";
        opp.Value = 5500m;
        await service.UpdateAsync(opp);

        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("After update", loaded!.Title);
        Assert.Equal(5500m, loaded.Value);
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

    [Fact]
    public async Task GetAllAsync_FiltersByStage()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        await service.CreateAsync(new Opportunity { Title = "Lead opp", CustomerName = "A", Value = 1000m, Stage = OpportunityStage.Lead });
        await service.CreateAsync(new Opportunity { Title = "Proposal opp", CustomerName = "B", Value = 2000m, Stage = OpportunityStage.Proposal });

        var proposals = await service.GetAllAsync(stage: OpportunityStage.Proposal);

        Assert.Single(proposals);
        Assert.Equal("Proposal opp", proposals[0].Title);
    }

    [Fact]
    public async Task CreateAsync_LogsAuditEntry_WhenAuditServiceProvided()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new OpportunityService(db, auditService);

        await service.CreateAsync(new Opportunity
        {
            Title = "Audited create",
            CustomerName = "Audit Co",
            Value = 15000m,
            Stage = OpportunityStage.Lead
        });

        var entries = await auditService.GetRecentAsync();
        var createEntry = entries.First(e => e.Action == "CREATE" && e.EntityType == "Opportunity");
        Assert.Equal("Audited create", createEntry.EntityReference);
    }
}