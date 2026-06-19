using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class OpportunityServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, OpportunityService Service) CreateHarness(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
        var provider = services.BuildServiceProvider();
        var cache = new TenantDistributedCacheService(
            provider.GetRequiredService<IDistributedCache>(),
            tenantProvider.Object,
            provider.GetRequiredService<IOptions<CacheOptions>>());

        return (db, cache, new OpportunityService(db, cache: cache));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Opportunity>().Add(new Opportunity { TenantId = tenantId, Title = "Original Deal", Value = 10000m });
            await db.SaveChangesAsync();

            Assert.Equal("Original Deal", (await service.GetAllAsync())[0].Title);
            (await db.Set<Opportunity>().FirstAsync()).Title = "Mutated Deal";
            await db.SaveChangesAsync();
            Assert.Equal("Original Deal", (await service.GetAllAsync())[0].Title);

            cache.InvalidateCategory("opportunities");
            Assert.Equal("Mutated Deal", (await service.GetAllAsync())[0].Title);
        }
    }

    [Fact]
    public async Task CreateAsync_InvalidatesOpportunityListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAsync(new Opportunity { TenantId = tenantId, Title = "First Opp", Value = 5000m });
            Assert.Single(await service.GetAllAsync());
            await service.CreateAsync(new Opportunity { TenantId = tenantId, Title = "Second Opp", Value = 8000m });
            Assert.Equal(2, (await service.GetAllAsync()).Count);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSearch_BypassesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Opportunity>().Add(new Opportunity { TenantId = tenantId, Title = "Alpha substation", Value = 50000m });
            db.Set<Opportunity>().Add(new Opportunity { TenantId = tenantId, Title = "Beta lighting", Value = 12000m });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            var beta = await db.Set<Opportunity>().FirstAsync(o => o.Title == "Beta lighting");
            beta.Title = "Beta Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Beta Mutated", (await service.GetAllAsync(search: "Beta", pageSize: 50))[0].Title);
        }
    }

    [Fact]
    public async Task GetAllAsync_WithWhitespaceSearch_UsesCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Opportunity>().Add(new Opportunity { TenantId = tenantId, Title = "Cached Opp", Value = 1000m });
            await db.SaveChangesAsync();
            await service.GetAllAsync(pageSize: 50);

            (await db.Set<Opportunity>().FirstAsync()).Title = "DB Mutated";
            await db.SaveChangesAsync();

            Assert.Equal("Cached Opp", (await service.GetAllAsync(search: "   ", pageSize: 50))[0].Title);
        }
    }

    [Fact]
    public async Task AdvanceStageAsync_InvalidatesOpportunityListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var id = await service.CreateAsync(new Opportunity
            {
                TenantId = tenantId,
                Title = "Pipeline Deal",
                Value = 25000m,
                Stage = OpportunityStage.Lead
            });
            await service.GetAllAsync();
            await service.AdvanceStageAsync(id);
            Assert.Equal(OpportunityStage.Qualified, (await service.GetAllAsync())[0].Stage);
        }
    }

    [Fact]
    public async Task GetAllAsync_StageFilter_UsesSeparateCacheEntries()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Opportunity>().Add(new Opportunity
            {
                TenantId = tenantId,
                Title = "Lead Opp",
                Value = 1000m,
                Stage = OpportunityStage.Lead
            });
            db.Set<Opportunity>().Add(new Opportunity
            {
                TenantId = tenantId,
                Title = "Proposal Opp",
                Value = 2000m,
                Stage = OpportunityStage.Proposal
            });
            await db.SaveChangesAsync();

            Assert.Equal(2, (await service.GetAllAsync(pageSize: 50)).Count);
            Assert.Single(await service.GetAllAsync(stage: OpportunityStage.Lead, pageSize: 50));

            (await db.Set<Opportunity>().FirstAsync(o => o.Stage == OpportunityStage.Lead)).Stage = OpportunityStage.Proposal;
            await db.SaveChangesAsync();

            Assert.Equal(2, (await service.GetAllAsync(pageSize: 50)).Count);
            Assert.Single(await service.GetAllAsync(stage: OpportunityStage.Lead, pageSize: 50));

            cache.InvalidateCategory("opportunities");
            Assert.Empty(await service.GetAllAsync(stage: OpportunityStage.Lead, pageSize: 50));
        }
    }
}