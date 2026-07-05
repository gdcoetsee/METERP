using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Interfaces;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Verifies commercial usage counter increments are reliable and tenant-isolated
/// (isolated DbContext scope per increment — no fire-and-forget).
/// </summary>
public class TenantServiceTests
{
    private readonly Mock<ITenantProvider> _tenantProviderMock = new();
    private readonly Mock<ICurrentUserService> _currentUserMock = new();

    public TenantServiceTests()
    {
        _currentUserMock.Setup(s => s.UserId).Returns(Guid.NewGuid());
        _currentUserMock.Setup(s => s.UserName).Returns("test-user");
    }

    private AppDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options, _tenantProviderMock.Object, _currentUserMock.Object);
    }

    private IServiceScopeFactory CreateScopeFactory(string databaseName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(_tenantProviderMock.Object);
        services.AddSingleton(_currentUserMock.Object);

        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private TenantService CreateService(string databaseName)
    {
        var db = CreateDbContext(databaseName);
        var scopeFactory = CreateScopeFactory(databaseName);
        return new TenantService(db, scopeFactory);
    }

    private static async Task<Tenant> SeedTenantAsync(AppDbContext db, string name, string subdomain)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Subdomain = subdomain,
            Tier = SubscriptionTier.Starter
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task IncrementQuoteCountAsync_IncrementsTotalAndPeriod_AndUpdatesLastActivity()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");
        var before = DateTime.UtcNow.AddSeconds(-1);

        await service.IncrementQuoteCountAsync(tenant.Id);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.TotalQuotesCreated);
        Assert.Equal(1, updated.PeriodQuotesCreated);
        Assert.NotNull(updated.LastActivityUtc);
        Assert.True(updated.LastActivityUtc >= before);
    }

    [Fact]
    public async Task IncrementJobCountAsync_IncrementsCounters()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");

        await service.IncrementJobCountAsync(tenant.Id);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.TotalJobsCreated);
        Assert.Equal(1, updated.PeriodJobsCreated);
    }

    [Fact]
    public async Task IncrementInvoiceCountAsync_TracksRevenueAndCounters()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");

        await service.IncrementInvoiceCountAsync(tenant.Id, 12500.50m);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.TotalInvoicesIssued);
        Assert.Equal(1, updated.PeriodInvoicesIssued);
        Assert.Equal(12500.50m, updated.TotalRevenueBilled);
    }

    [Fact]
    public async Task IncrementAiCallCountAsync_IncrementsCounters()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");

        await service.IncrementAiCallCountAsync(tenant.Id);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(1, updated.TotalAiCalls);
        Assert.Equal(1, updated.PeriodAiCalls);
    }

    [Fact]
    public async Task IncrementCounters_AreTenantIsolated()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var acme = await SeedTenantAsync(db, "Acme", "acme");
        var beta = await SeedTenantAsync(db, "Beta", "beta");

        await service.IncrementQuoteCountAsync(acme.Id);
        await service.IncrementQuoteCountAsync(acme.Id);
        await service.IncrementJobCountAsync(beta.Id);
        await service.IncrementAiCallCountAsync(beta.Id);
        await service.IncrementAiCallCountAsync(beta.Id);

        var acmeUpdated = await service.GetByIdAsync(acme.Id);
        var betaUpdated = await service.GetByIdAsync(beta.Id);

        Assert.NotNull(acmeUpdated);
        Assert.NotNull(betaUpdated);
        Assert.Equal(2, acmeUpdated.TotalQuotesCreated);
        Assert.Equal(0, acmeUpdated.TotalJobsCreated);
        Assert.Equal(0, acmeUpdated.TotalAiCalls);
        Assert.Equal(0, acmeUpdated.PeriodAiCalls);
        Assert.Equal(0, betaUpdated.TotalQuotesCreated);
        Assert.Equal(1, betaUpdated.TotalJobsCreated);
        Assert.Equal(2, betaUpdated.TotalAiCalls);
        Assert.Equal(2, betaUpdated.PeriodAiCalls);
    }

    [Fact]
    public async Task UpdateAsync_PersistsTenantAiSettingsFields()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");

        tenant.AiProvider = AiProviderProfiles.Groq;
        tenant.AiBaseUrl = "https://api.groq.com/openai/v1/";
        tenant.AiModel = "llama-3.3-70b-versatile";
        tenant.AiUseTenantKey = true;
        tenant.AiApiKeyEncrypted = "encrypted-key-blob";

        await service.UpdateAsync(tenant);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(AiProviderProfiles.Groq, updated.AiProvider);
        Assert.Equal("https://api.groq.com/openai/v1", updated.AiBaseUrl);
        Assert.Equal("llama-3.3-70b-versatile", updated.AiModel);
        Assert.True(updated.AiUseTenantKey);
        Assert.Equal("encrypted-key-blob", updated.AiApiKeyEncrypted);
    }

    [Fact]
    public async Task IncrementQuoteCountAsync_NoOpForEmptyTenantId()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");

        await service.IncrementQuoteCountAsync(Guid.Empty);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(0, updated.TotalQuotesCreated);
    }

    [Theory]
    [InlineData(nameof(TenantService.IncrementJobCountAsync))]
    [InlineData(nameof(TenantService.IncrementInvoiceCountAsync))]
    [InlineData(nameof(TenantService.IncrementAiCallCountAsync))]
    public async Task IncrementCounters_UpdateLastActivityUtc(string methodName)
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");
        var before = DateTime.UtcNow.AddSeconds(-1);

        switch (methodName)
        {
            case nameof(TenantService.IncrementJobCountAsync):
                await service.IncrementJobCountAsync(tenant.Id);
                break;
            case nameof(TenantService.IncrementInvoiceCountAsync):
                await service.IncrementInvoiceCountAsync(tenant.Id, 100m);
                break;
            case nameof(TenantService.IncrementAiCallCountAsync):
                await service.IncrementAiCallCountAsync(tenant.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(methodName));
        }

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.NotNull(updated.LastActivityUtc);
        Assert.True(updated.LastActivityUtc >= before);
    }

    [Fact]
    public async Task IncrementQuoteCountAsync_ResetsStalePeriodCounters_BeforeIncrementing()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = CreateDbContext(dbName);
        var service = CreateService(dbName);
        var tenant = await SeedTenantAsync(db, "Acme", "acme");
        tenant.UsagePeriodStartUtc = QuotaService.GetCurrentPeriodStartUtc().AddMonths(-2);
        tenant.PeriodQuotesCreated = 20;
        tenant.TotalQuotesCreated = 50;
        await db.SaveChangesAsync();

        await service.IncrementQuoteCountAsync(tenant.Id);

        var updated = await service.GetByIdAsync(tenant.Id);
        Assert.NotNull(updated);
        Assert.Equal(51, updated.TotalQuotesCreated);
        Assert.Equal(1, updated.PeriodQuotesCreated);
        Assert.Equal(QuotaService.GetCurrentPeriodStartUtc(), updated.UsagePeriodStartUtc);
    }

    [Fact]
    public async Task IncrementQuoteCountAsync_ConcurrentIncrements_AllPersisted()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton(_tenantProviderMock.Object);
        services.AddSingleton(_currentUserMock.Object);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        Guid tenantId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Concurrent",
                Subdomain = "concurrent",
                Tier = SubscriptionTier.Starter
            });
            await db.SaveChangesAsync();
        }

        await using var readScope = provider.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new TenantService(readDb, scopeFactory);

        const int parallelIncrements = 12;
        await Task.WhenAll(Enumerable.Range(0, parallelIncrements)
            .Select(_ => service.IncrementQuoteCountAsync(tenantId)));

        var updated = await service.GetByIdAsync(tenantId);
        Assert.NotNull(updated);
        Assert.Equal(parallelIncrements, updated.TotalQuotesCreated);
        Assert.Equal(parallelIncrements, updated.PeriodQuotesCreated);
    }
}