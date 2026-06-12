using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AssetServiceTests
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
    public async Task CreateAsync_AssignsAssetNumber_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Mine Co" });

        var id = await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = "11kV Transformer",
            AssetType = "Transformer"
        });

        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.StartsWith("AST-", loaded.AssetNumber);
    }

    [Fact]
    public async Task GetAllAsync_FiltersBySearchTerm()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Acme" });

        await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Substation Transformer", Location = "North Shaft" });
        await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Panel Board A", Location = "Office" });

        var results = await service.GetAllAsync("transformer");

        Assert.Single(results);
        Assert.Equal("Substation Transformer", results[0].Name);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesAssetStatus()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Motor 1", Status = AssetStatus.Operational });

        await service.UpdateStatusAsync(id, AssetStatus.UnderMaintenance);

        var loaded = await service.GetByIdAsync(id);
        Assert.Equal(AssetStatus.UnderMaintenance, loaded!.Status);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_AppendsToNotes()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "TRF-1", Notes = "Existing" });
        var jobId = Guid.NewGuid();

        await service.AddMaintenanceNoteAsync(id, "Oil sample taken", jobId);

        var loaded = await service.GetByIdAsync(id);
        Assert.Contains("Existing", loaded!.Notes);
        Assert.Contains("Oil sample taken", loaded.Notes);
        Assert.Contains(jobId.ToString(), loaded.Notes);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesAsset()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "To Remove" });

        await service.DeleteAsync(id);

        Assert.Null(await service.GetByIdAsync(id));
        var deleted = await db.Set<Asset>().IgnoreQueryFilters().FirstAsync(a => a.Id == id);
        Assert.True(deleted.IsDeleted);
    }
}