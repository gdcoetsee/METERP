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

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public Guid TenantId => Guid.Empty;
        public string? UserName => "test";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
    }
}