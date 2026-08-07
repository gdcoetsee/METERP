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

    private static async Task<Guid> SeedQuoteAsync(AppDbContext db, Guid tenantId, Guid? customerId = null)
    {
        Guid custId;
        if (customerId is { } existing && existing != Guid.Empty)
        {
            custId = existing;
        }
        else
        {
            custId = Guid.NewGuid();
            db.Set<Customer>().Add(new Customer { Id = custId, TenantId = tenantId, Name = "Quote Customer" });
        }

        var quote = new Quote
        {
            TenantId = tenantId,
            CustomerId = custId,
            QuoteNumber = $"Q-{Guid.NewGuid():N}"[..12],
            Status = QuoteStatus.Draft
        };
        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new OpportunityService(db);

        Assert.Null(await service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_Throws_WhenOpportunityMissing()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new OpportunityService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MarkConvertedToQuoteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_Throws_WhenAlreadyLinkedToDifferentQuote()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "Plant upgrade",
            Stage = OpportunityStage.Qualified,
            Value = 50000m
        });
        var firstQuote = await SeedQuoteAsync(db, tenantId);
        var secondQuote = await SeedQuoteAsync(db, tenantId);
        await service.MarkConvertedToQuoteAsync(oppId, firstQuote);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MarkConvertedToQuoteAsync(oppId, secondQuote));
        Assert.Contains("already linked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_Throws_WhenQuoteMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "No quote",
            Stage = OpportunityStage.Qualified,
            Value = 1000m
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MarkConvertedToQuoteAsync(oppId, Guid.NewGuid()));
        Assert.Contains("quote", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MarkConvertedToQuoteAsync_Throws_WhenClosedLost()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var oppId = await service.CreateAsync(new Opportunity
        {
            Title = "Lost deal",
            Stage = OpportunityStage.ClosedLost,
            Value = 1000m
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MarkConvertedToQuoteAsync(oppId, Guid.NewGuid()));
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
    public async Task CreateAsync_ThrowsWhenValueTooHigh()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = "Impossible deal",
                Value = 100_000_001m
            }));
        Assert.Contains("100,000,000", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = "Note deal",
                Value = 1000m,
                Notes = new string('N', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsNotesAt2000Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Note deal ok",
            Value = 1000m,
            Notes = new string('N', 2000)
        });
        var saved = await db.Set<Opportunity>().FirstAsync(o => o.Id == id);
        Assert.Equal(2000, saved.Notes!.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCustomerNameTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = "Name deal",
                Value = 1000m,
                CustomerName = new string('C', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsTitleAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var id = await service.CreateAsync(new Opportunity
        {
            Title = new string('T', 200),
            Value = 1000m
        });
        var saved = await db.Set<Opportunity>().FirstAsync(o => o.Id == id);
        Assert.Equal(200, saved.Title.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenTitleTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = new string('T', 201),
                Value = 1000m
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenExpectedCloseTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = "Far deal",
                Value = 1000m,
                ExpectedClose = DateTime.UtcNow.Date.AddYears(3)
            }));
        Assert.Contains("2 years", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAllAsync_FiltersByCustomerName()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        await service.CreateAsync(new Opportunity { Title = "Deal A", CustomerName = "Mining Co", Value = 50000m });
        await service.CreateAsync(new Opportunity { Title = "Deal B", CustomerName = "Retail Plaza", Value = 12000m });

        var results = await service.GetAllAsync("mining");

        Assert.Single(results);
        Assert.Equal("Mining Co", results[0].CustomerName);
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
    public async Task CreateAsync_ThrowsWhenCustomerIdMissing()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new OpportunityService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Opportunity
            {
                Title = "Orphan opp",
                CustomerId = Guid.NewGuid(),
                Value = 1000m
            }));
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
    public async Task AdvanceStageAsync_Throws_WhenOpportunityMissing()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new OpportunityService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AdvanceStageAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AdvanceStageAsync_ThrowsFromNegotiation_InsteadOfAutoClosedLost()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Negotiating",
            Value = 10000m,
            Stage = OpportunityStage.Negotiation
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdvanceStageAsync(id));
        Assert.Equal(OpportunityStage.Negotiation, (await service.GetByIdAsync(id))!.Stage);
    }

    [Fact]
    public async Task DeleteAsync_IsNoOp_WhenOpportunityMissing()
    {
        using var db = CreateContext(Guid.NewGuid());
        var service = new OpportunityService(db);

        await service.DeleteAsync(Guid.NewGuid());

        Assert.Empty(await service.GetAllAsync());
    }

    [Fact]
    public async Task AdvanceStageAsync_ThrowsFromClosedLost()
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdvanceStageAsync(id));

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
    public void BuildAiScopeText_UsesLinkedCustomer_WhenCustomerNameMissing()
    {
        var service = new OpportunityService(CreateContext(Guid.NewGuid()));
        var text = service.BuildAiScopeText(new Opportunity
        {
            Title = "Substation upgrade",
            Value = 88000m,
            ExpectedClose = new DateTime(2026, 8, 15),
            Customer = new Customer { Name = "Linked Customer Ltd" }
        });

        Assert.Contains("Linked Customer Ltd", text);
        Assert.Contains("Substation upgrade", text);
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
        var quoteId = await SeedQuoteAsync(db, tenantId);

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
        var quoteId = await SeedQuoteAsync(db, tenantId);

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
        var quoteId = await SeedQuoteAsync(db, tenantId);

        await service.MarkConvertedToQuoteAsync(oppId, quoteId);

        var loaded = await service.GetByIdAsync(oppId);
        Assert.NotNull(loaded);
        Assert.Equal(quoteId, loaded!.QuoteId);
        Assert.Equal(OpportunityStage.Proposal, loaded.Stage);
    }

    [Fact]
    public async Task UpdateAsync_ClosedWon_RequiresCustomerAndValue()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Almost won",
            Stage = OpportunityStage.Negotiation,
            Value = 0m
        });

        var opp = await service.GetByIdAsync(id);
        opp!.Stage = OpportunityStage.ClosedWon;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(opp));
        Assert.Contains("Customer", ex.Message, StringComparison.OrdinalIgnoreCase);

        opp.CustomerName = "Acme";
        opp.Value = 0m;
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(opp));
        Assert.Contains("value", ex.Message, StringComparison.OrdinalIgnoreCase);

        opp.Value = 5000m;
        await service.UpdateAsync(opp);
        Assert.Equal(OpportunityStage.ClosedWon, (await service.GetByIdAsync(id))!.Stage);
    }

    [Fact]
    public async Task UpdateAsync_ClosedWon_ThrowsWhenLinkedCustomerDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Gone Co" });
        await db.SaveChangesAsync();

        var id = await service.CreateAsync(new Opportunity
        {
            Title = "With customer",
            Stage = OpportunityStage.Negotiation,
            Value = 10000m,
            CustomerId = customerId
        });

        var customer = await db.Set<Customer>().FirstAsync(c => c.Id == customerId);
        customer.IsDeleted = true;
        await db.SaveChangesAsync();

        var opp = await service.GetByIdAsync(id);
        opp!.Stage = OpportunityStage.ClosedWon;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(opp));
        Assert.Contains("deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task UpdateAsync_LogsAuditEntry()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new OpportunityService(db, auditService);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Update audit opp",
            CustomerName = "Audit Co",
            Value = 7000m,
            Stage = OpportunityStage.Lead
        });
        var opp = await service.GetByIdAsync(id);
        Assert.NotNull(opp);
        opp!.Title = "Updated title";
        await service.UpdateAsync(opp);

        var updateEntry = (await auditService.GetRecentAsync()).First(e => e.Action == "UPDATE" && e.EntityReference == "Updated title");
        Assert.Equal("Opportunity", updateEntry.EntityType);
    }

    [Fact]
    public async Task DeleteAsync_LogsAuditEntry()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var auditService = new AuditService(db, currentUser.Object);
        var service = new OpportunityService(db, auditService);
        var id = await service.CreateAsync(new Opportunity
        {
            Title = "Delete audit opp",
            CustomerName = "Audit Co",
            Value = 2000m
        });

        await service.DeleteAsync(id);

        var deleteEntry = (await auditService.GetRecentAsync()).First(e => e.Action == "DELETE");
        Assert.Equal("Opportunity", deleteEntry.EntityType);
        Assert.Equal("Delete audit opp", deleteEntry.EntityReference);
        Assert.Contains("Soft deleted", deleteEntry.Details);
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
    public async Task GetAllAsync_RespectsPagination()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new OpportunityService(db);
        for (var i = 0; i < 5; i++)
        {
            await service.CreateAsync(new Opportunity
            {
                Title = $"Opp {i}",
                CustomerName = "Pager",
                Value = 1000m + i
            });
        }

        var page1 = await service.GetAllAsync(page: 1, pageSize: 2);
        var page2 = await service.GetAllAsync(page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
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