using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class DocumentSequenceServiceTests
{
    [Fact]
    public async Task GetNextNumberAsync_ReturnsSequentialNumbersPerYear()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        var first = await service.GetNextNumberAsync("Quote", "Q");
        var second = await service.GetNextNumberAsync("Quote", "Q");

        Assert.Matches(@"^Q-\d{4}-00001$", first);
        Assert.Matches(@"^Q-\d{4}-00002$", second);
    }

    [Fact]
    public async Task GetNextNumberAsync_RejectsEmptyDocumentType()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetNextNumberAsync("  ", "Q"));
    }

    [Fact]
    public async Task GetNextNumberAsync_RejectsDocumentTypeTooLong()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetNextNumberAsync(new string('D', 51), "Q"));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task GetNextNumberAsync_AcceptsDocumentTypeAt50Characters()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        var number = await service.GetNextNumberAsync(new string('D', 50), "Q");
        Assert.StartsWith("Q-", number);
    }

    [Fact]
    public async Task GetNextNumberAsync_RejectsPrefixTooLong()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetNextNumberAsync("Quote", new string('P', 21)));
        Assert.Contains("20 characters", ex.Message);
    }

    [Fact]
    public async Task GetNextNumberAsync_AcceptsPrefixAt20Characters()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        var prefix = new string('P', 20);
        var number = await service.GetNextNumberAsync("Quote", prefix);
        Assert.StartsWith(prefix + "-", number);
    }

    [Fact]
    public async Task GetNextNumberAsync_RejectsEmptyPrefix()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seq-{Guid.NewGuid():N}")
            .Options;

        await using var db = new AppDbContext(options, tenantProvider.Object, new TestCurrentUser());
        var service = new DocumentSequenceService(db, tenantProvider.Object);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetNextNumberAsync("Quote", "  "));
    }

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid TenantId => Guid.Empty;
        public Guid? CustomerId => null;
        public string? UserName => "test";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        public bool IsCustomerPortalUser => false;
    }
}