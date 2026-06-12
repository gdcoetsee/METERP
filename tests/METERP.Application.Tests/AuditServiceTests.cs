using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AuditServiceTests
{
    private AppDbContext CreateContext(Guid tenantId, string userName = "admin@acme.demo")
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns(userName);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProvider.Object, currentUser.Object);
    }

    [Fact]
    public async Task LogAsync_PersistsEntry_WithCurrentUser()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserName).Returns("admin@acme.demo");
        var service = new AuditService(db, currentUser.Object);

        await service.LogAsync("CREATE", "Quote", "Q-2026-001", "Total R 9,200");

        var rows = await service.GetRecentAsync();
        var row = Assert.Single(rows);
        Assert.Equal("admin@acme.demo", row.UserEmail);
        Assert.Equal("CREATE", row.Action);
        Assert.Equal("Quote", row.EntityType);
        Assert.Contains("Q-2026-001", row.EntityReference);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesHeaderAndRows()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateContext(tenantId);
        var service = new AuditService(db, new Mock<ICurrentUserService>().Object);

        await service.LogAsync("INVOICE", "Invoice", "INV-100", "Posted");

        var csv = await service.ExportCsvAsync();

        Assert.Contains("OccurredAtUtc,UserEmail,Action", csv);
        Assert.Contains("INV-100", csv);
        Assert.Contains("INVOICE", csv);
    }
}