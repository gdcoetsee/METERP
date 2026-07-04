using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Phase 4: tenant isolation for spine-feeding supporting modules (global query filters).
/// </summary>
public class SupportingModuleTenantIsolationTests
{
    private static AppDbContext CreateContext(string dbName, Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task CustomerService_GetAllAsync_ReturnsOnlyCurrentTenantCustomers()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed each tenant from its own context so query filters bind to the correct tenant.
        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Customer>().Add(new Customer { TenantId = tenantA, Name = "Acme Tenant A" });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Customer>().Add(new Customer { TenantId = tenantB, Name = "Beta Tenant B" });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var resultsA = await new CustomerService(dbA).GetAllAsync();
        Assert.Single(resultsA);
        Assert.Equal("Acme Tenant A", resultsA[0].Name);

        await using var dbB = CreateContext(dbName, tenantB);
        var resultsB = await new CustomerService(dbB).GetAllAsync();
        Assert.Single(resultsB);
        Assert.Equal("Beta Tenant B", resultsB[0].Name);
    }

    [Fact]
    public async Task OpportunityService_GetAllAsync_ReturnsOnlyCurrentTenantOpportunities()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<Opportunity>().Add(new Opportunity { TenantId = tenantA, Title = "Opp A", CustomerName = "A", Value = 1000m });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<Opportunity>().Add(new Opportunity { TenantId = tenantB, Title = "Opp B", CustomerName = "B", Value = 2000m });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var resultsA = await new OpportunityService(dbA).GetAllAsync();
        Assert.Single(resultsA);
        Assert.Equal("Opp A", resultsA[0].Title);

        await using var dbB = CreateContext(dbName, tenantB);
        var resultsB = await new OpportunityService(dbB).GetAllAsync();
        Assert.Single(resultsB);
        Assert.Equal("Opp B", resultsB[0].Title);
    }

    [Fact]
    public async Task InventoryService_GetAllItemsAsync_ReturnsOnlyCurrentTenantItems()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var seedA = CreateContext(dbName, tenantA))
        {
            seedA.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantA, Sku = "A-1", Name = "Tenant A fuse", QuantityOnHand = 5, ReorderLevel = 1 });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = CreateContext(dbName, tenantB))
        {
            seedB.Set<InventoryItem>().Add(new InventoryItem { TenantId = tenantB, Sku = "B-1", Name = "Tenant B fuse", QuantityOnHand = 5, ReorderLevel = 1 });
            await seedB.SaveChangesAsync();
        }

        await using var dbA = CreateContext(dbName, tenantA);
        var itemsA = await new InventoryService(dbA).GetAllItemsAsync();
        Assert.Single(itemsA);
        Assert.Equal("A-1", itemsA[0].Sku);

        await using var dbB = CreateContext(dbName, tenantB);
        var itemsB = await new InventoryService(dbB).GetAllItemsAsync();
        Assert.Single(itemsB);
        Assert.Equal("B-1", itemsB[0].Sku);
    }
}