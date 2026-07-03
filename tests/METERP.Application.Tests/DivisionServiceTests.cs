using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class DivisionServiceTests
{
    private static (DivisionService Service, AppDbContext Db, Guid TenantId) Create()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"division-{Guid.NewGuid():N}")
            .Options;

        var db = new AppDbContext(options, tenantProvider.Object, new Mock<ICurrentUserService>().Object);
        return (new DivisionService(db), db, tenantId);
    }

    [Fact]
    public async Task GetAllAsync_ActiveOnly_ExcludesInactive()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            db.Set<Division>().AddRange(
                new Division { TenantId = tenantId, Code = "E", Name = "Electrical", IsActive = true },
                new Division { TenantId = tenantId, Code = "X", Name = "Closed", IsActive = false });
            await db.SaveChangesAsync();

            var active = await service.GetAllAsync(activeOnly: true);
            var all = await service.GetAllAsync(activeOnly: false);

            Assert.Single(active);
            Assert.Equal("Electrical", active[0].Name);
            Assert.Equal(2, all.Count);
        }
    }

    [Fact]
    public async Task CreateAsync_PersistsAndReturnsId()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var id = await service.CreateAsync(new Division
            {
                TenantId = tenantId,
                Code = "M",
                Name = "Maintenance"
            });

            var saved = await db.Set<Division>().FirstAsync(d => d.Id == id);
            Assert.Equal("Maintenance", saved.Name);
            Assert.True(saved.IsActive);
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var (service, db, tenantId) = Create();
        await using (db)
        {
            var division = new Division { TenantId = tenantId, Code = "H", Name = "HV" };
            db.Set<Division>().Add(division);
            await db.SaveChangesAsync();

            division.Name = "High Voltage";
            division.IsActive = false;
            await service.UpdateAsync(division);

            var saved = await db.Set<Division>().FirstAsync(d => d.Id == division.Id);
            Assert.Equal("High Voltage", saved.Name);
            Assert.False(saved.IsActive);
        }
    }
}