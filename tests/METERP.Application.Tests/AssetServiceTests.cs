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
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            JobNumber = "J-MAINT-1",
            Title = "Service call",
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();

        await service.AddMaintenanceNoteAsync(id, "Oil sample taken", job.Id);

        var loaded = await service.GetByIdAsync(id);
        Assert.Contains("Existing", loaded!.Notes);
        Assert.Contains("Oil sample taken", loaded.Notes);
        Assert.Contains("J-MAINT-1", loaded.Notes);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_ThrowsWhenJobMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "TRF-2" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMaintenanceNoteAsync(id, "Note", Guid.NewGuid()));
        Assert.Contains("job", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatusAsync_Decommission_ThrowsWhenAssignedToOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = "Vehicle",
            Status = AssetStatus.Operational
        });
        db.Set<Job>().Add(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            AssetId = id,
            JobNumber = "J-ASSET",
            Title = "Open",
            Status = JobStatus.InProgress
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateStatusAsync(id, AssetStatus.Decommissioned));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_ThrowsWhenJobSoftDeleted()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Title = "Maint job",
            JobNumber = "J-MAINT-DEL",
            Status = JobStatus.InProgress
        };
        db.Set<Job>().Add(job);
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Panel" });
        await db.SaveChangesAsync();

        job.IsDeleted = true;
        await db.SaveChangesAsync();

        var delEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddMaintenanceNoteAsync(id, "Oil sample", job.Id));
        Assert.Contains("deleted", delEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_AllowsClosedJobForCompliance()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var job = new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            Title = "Closed maint",
            JobNumber = "J-MAINT-CL",
            Status = JobStatus.Closed
        };
        db.Set<Job>().Add(job);
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Transformer" });
        await db.SaveChangesAsync();

        await service.AddMaintenanceNoteAsync(id, "Post-close inspection", job.Id);
        var asset = await service.GetByIdAsync(id);
        Assert.Contains("J-MAINT-CL", asset!.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_AcceptsNoteAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "TRF-OK" });

        await service.AddMaintenanceNoteAsync(id, new string('N', 500));
        var asset = await service.GetByIdAsync(id);
        Assert.Contains(new string('N', 500), asset!.Notes);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_ThrowsWhenNoteTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "TRF-3" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddMaintenanceNoteAsync(id, new string('N', 501)));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task AddMaintenanceNoteAsync_ThrowsWhenNoteTooShort()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "TRF-4" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AddMaintenanceNoteAsync(id, "ab"));
        Assert.Contains("3 characters", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_PersistsLocationAndSerialNumber()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = "Motor 1",
            Location = "Shaft A"
        });

        var asset = await service.GetByIdAsync(id);
        Assert.NotNull(asset);
        asset!.Location = "Shaft B";
        asset.SerialNumber = "SN-88421";
        await service.UpdateAsync(asset);

        var reloaded = await service.GetByIdAsync(id);
        Assert.Equal("Shaft B", reloaded!.Location);
        Assert.Equal("SN-88421", reloaded.SerialNumber);
    }

    [Fact]
    public async Task GetByIdAsync_IncludesCustomerNavigation()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Linked Customer" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Panel" });

        var loaded = await service.GetByIdAsync(id);
        Assert.NotNull(loaded?.Customer);
        Assert.Equal("Linked Customer", loaded.Customer.Name);
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

    [Fact]
    public async Task DeleteAsync_ThrowsWhenAssetAssignedToOpenJob()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var assetId = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Crane" });

        db.Set<Job>().Add(new Job
        {
            TenantId = tenantId,
            CustomerId = customerId,
            AssetId = assetId,
            JobNumber = "J-AST",
            Title = "Lift",
            Status = JobStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(assetId));
        Assert.Contains("open jobs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNameTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = new string('A', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_AcceptsNameAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var id = await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = new string('A', 200)
        });
        var saved = await db.Set<Asset>().FirstAsync(a => a.Id == id);
        Assert.Equal(200, saved.Name.Length);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenNotesTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Panel",
                Notes = new string('N', 2001)
            }));
        Assert.Contains("2000 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenAssetNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Panel",
                AssetNumber = new string('A', 51)
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenSerialNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Panel",
                SerialNumber = new string('S', 101)
            }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenLocationTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Panel",
                Location = new string('L', 201)
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenAssetTypeTooLong()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Panel",
                AssetType = new string('T', 101)
            }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenCustomerMissing()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset { Name = "Orphan", CustomerId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenSerialNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = "TRF-A",
            SerialNumber = "SN-UNIQUE-1"
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "TRF-B",
                SerialNumber = "SN-UNIQUE-1"
            }));
        Assert.Contains("serial", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenAssetNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        await db.SaveChangesAsync();

        await service.CreateAsync(new Asset
        {
            CustomerId = customerId,
            Name = "Gen A",
            AssetNumber = "AST-FIXED-1"
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new Asset
            {
                CustomerId = customerId,
                Name = "Gen B",
                AssetNumber = "AST-FIXED-1"
            }));
        Assert.Contains("Asset number", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_PreservesAssetNumber()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateContext(tenantId);
        var service = new AssetService(db);
        var customerId = Guid.NewGuid();
        db.Set<Customer>().Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Client" });
        var id = await service.CreateAsync(new Asset { CustomerId = customerId, Name = "Genset" });
        var asset = await service.GetByIdAsync(id);
        Assert.NotNull(asset);
        var number = asset!.AssetNumber;

        asset.Name = "  Genset 50kVA  ";
        asset.AssetNumber = "HACKED";
        await service.UpdateAsync(asset);

        var reloaded = await service.GetByIdAsync(id);
        Assert.Equal("Genset 50kVA", reloaded!.Name);
        Assert.Equal(number, reloaded.AssetNumber);
    }
}