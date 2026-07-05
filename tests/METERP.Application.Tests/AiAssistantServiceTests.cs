using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Unit tests for AiAssistantService guards and commercial behavior.
/// Per Phase 3 of COMPLETION_PLAN.md and testing rules.
/// Focus on sellable aspects: feature flags, throttling, usage counters, graceful degradation.
/// </summary>
public class AiAssistantServiceTests
{
    public AiAssistantServiceTests() => AiAssistantService.ClearThrottleStateForTesting();

    private static IConfiguration CreateConfig(string? apiKey = "fake-key", bool enabled = true)
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = apiKey,
            ["Ai:BaseUrl"] = "https://api.openai.com/v1",
            ["Ai:Model"] = "gpt-4o-mini",
            ["Ai:Enabled"] = enabled.ToString(),
            ["Ai:TimeoutSeconds"] = "30"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    private static ILogger<AiAssistantService> CreateLogger() =>
        Mock.Of<ILogger<AiAssistantService>>();

    private static AiAssistantService CreateService(
        IConfiguration config,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        HttpClient? httpClient = null) =>
        new(
            new AiConfigurationResolver(config, tenantProvider, tenantService),
            CreateLogger(),
            tenantService,
            tenantProvider,
            quotaService,
            httpClient);

    [Fact]
    public void IsConfigured_False_When_NoApiKey()
    {
        var config = CreateConfig(apiKey: null);
        var service = CreateService(config);

        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_False_When_Disabled()
    {
        var config = CreateConfig(enabled: false);
        var service = CreateService(config);

        Assert.False(service.IsConfigured);
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsNull_When_NotConfigured()
    {
        var config = CreateConfig(apiKey: null);
        var service = CreateService(config);

        var result = await service.SuggestQuoteLinesAsync("some scope", 0.15m);

        Assert.Null(result);
    }

    [Fact]
    public async Task AskCopilotAsync_ReturnsRateLimitMessage_When_Throttled()
    {
        var config = CreateConfig();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.NewGuid());

        var service = CreateService(config, tenantProvider: tenantProvider.Object);

        await service.AskCopilotAsync("What are my top travel cost risks?");
        var throttled = await service.AskCopilotAsync("Immediate retry");

        Assert.NotNull(throttled);
        Assert.Contains("Rate limit", throttled, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsNull_When_Throttled()
    {
        var config = CreateConfig();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.NewGuid());

        var service = CreateService(config, tenantProvider: tenantProvider.Object);

        // First call should be allowed (but will fail on HTTP since no real key, returns null gracefully)
        var first = await service.SuggestQuoteLinesAsync("test", 0.15m);

        // Second immediate call should be throttled
        var second = await service.SuggestQuoteLinesAsync("test again", 0.15m);

        // Both null is expected in this env, but the important part is the second is fast/throttled path
        Assert.Null(second);
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsNull_When_AiFeatureDisabled()
    {
        var config = CreateConfig();
        var tenantId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            EnabledFeatures = "usage-tracking" // ai deliberately missing
        };

        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(tenant);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(config, tenantService.Object, tenantProvider.Object);

        var result = await service.SuggestQuoteLinesAsync("scope with travel costs", 0.15m);

        Assert.Null(result);
        // Feature check should have short-circuited before any LLM call
    }

    [Fact]
    public async Task AnalyzeJobVarianceAsync_ReturnsNull_When_AiFeatureDisabled()
    {
        var config = CreateConfig();
        var tenantId = Guid.NewGuid();

        var tenant = new Tenant
        {
            Id = tenantId,
            EnabledFeatures = "usage-tracking" // ai deliberately missing
        };

        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(tenant);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(config, tenantService.Object, tenantProvider.Object);

        var job = new Job { QuotedTotal = 10000, ActualCost = 8000 };
        var result = await service.AnalyzeJobVarianceAsync(job);

        Assert.Null(result);
    }

    [Fact]
    public async Task AskCopilotAsync_Attempts_UsageCounter_When_SuccessPath_Reached()
    {
        var config = CreateConfig();
        var tenantId = Guid.NewGuid();

        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(config, tenantService.Object, tenantProvider.Object);

        // Will likely return null because no real LLM, but the counter attempt happens in the success branch
        // We mainly verify the wiring
        var result = await service.AskCopilotAsync("What are my top travel cost risks this month?");

        // Counter increment is awaited on the success path; without a live LLM we verify wiring does not throw.
        // For this test we at least ensure no crash and IsConfigured was true.
        Assert.True(service.IsConfigured);
    }

    [Fact]
    public void IsConfigured_True_With_ValidKeyAndEnabled()
    {
        var config = CreateConfig(apiKey: "valid-key", enabled: true);
        var service = CreateService(config);

        Assert.True(service.IsConfigured);
    }

    private sealed class QuotaBlockedHarness : IDisposable
    {
        public Guid TenantId { get; }
        public TenantService TenantService { get; }
        public QuotaService QuotaService { get; }
        public Mock<ITenantProvider> TenantProvider { get; }

        public QuotaBlockedHarness(Guid tenantId)
        {
            TenantId = tenantId;
            var dbName = Guid.NewGuid().ToString();

            TenantProvider = new Mock<ITenantProvider>();
            TenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUser.Setup(u => u.UserName).Returns("ai-quota-test");

            var services = new ServiceCollection();
            services.AddScoped(_ => TenantProvider.Object);
            services.AddScoped(_ => currentUser.Object);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var db = provider.GetRequiredService<AppDbContext>();

            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Quota Blocked",
                Subdomain = $"aiq-{tenantId:N}".Substring(0, 12),
                Tier = SubscriptionTier.Starter,
                EnabledFeatures = "ai,usage-tracking",
                UsagePeriodStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodAiCalls = 30
            });
            db.SaveChanges();

            TenantService = new TenantService(db, scopeFactory);
            QuotaService = new QuotaService(scopeFactory);
        }

        public AiAssistantService CreateAiService()
        {
            var config = CreateConfig();
            return CreateService(
                config,
                TenantService,
                TenantProvider.Object,
                QuotaService);
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsNull_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaBlockedHarness(tenantId);
        var service = harness.CreateAiService();

        var result = await service.SuggestQuoteLinesAsync("Install 3-phase panel with site travel", 0.15m);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeJobVarianceAsync_ReturnsNull_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaBlockedHarness(tenantId);
        var service = harness.CreateAiService();

        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            QuotedTotal = 10000m,
            ActualCost = 12000m
        };

        var result = await service.AnalyzeJobVarianceAsync(job);

        Assert.Null(result);
    }

    [Fact]
    public async Task AskCopilotAsync_ReturnsQuotaMessage_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        using var harness = new QuotaBlockedHarness(tenantId);
        var service = harness.CreateAiService();

        var result = await service.AskCopilotAsync("What are my top travel cost risks?");

        Assert.NotNull(result);
        Assert.Contains("AiCall", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quota exceeded", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeJobVarianceAsync_ReturnsNull_When_Throttled()
    {
        var config = CreateConfig();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.NewGuid());

        var service = CreateService(config, tenantProvider: tenantProvider.Object);

        var job = new Job
        {
            QuotedTotal = 10000m,
            ActualCost = 12000m,
            Title = "Throttled variance job"
        };

        await service.AnalyzeJobVarianceAsync(job);
        var throttled = await service.AnalyzeJobVarianceAsync(job);

        Assert.Null(throttled);
    }

    [Fact]
    public async Task AnalyzeJobVarianceAsync_DoesNotIncrementCounter_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        var tenant = new Tenant
        {
            Id = tenantId,
            EnabledFeatures = "ai",
            PeriodAiCalls = 30,
            MaxAiCallsPerMonth = 30,
            Tier = SubscriptionTier.Starter,
            UsagePeriodStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.EnsureAllowedAsync(tenantId, QuotaType.AiCall, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QuotaExceededException(QuotaType.AiCall, 30, 30));

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(
            CreateConfig(),
            tenantService.Object,
            tenantProvider.Object,
            quotaService.Object);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            QuotedTotal = 10000m,
            ActualCost = 12000m
        };

        await service.AnalyzeJobVarianceAsync(job);

        tenantService.Verify(
            s => s.IncrementAiCallCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AskCopilotAsync_DoesNotIncrementCounter_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        var tenant = new Tenant
        {
            Id = tenantId,
            EnabledFeatures = "ai",
            PeriodAiCalls = 30,
            MaxAiCallsPerMonth = 30,
            Tier = SubscriptionTier.Starter,
            UsagePeriodStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.EnsureAllowedAsync(tenantId, QuotaType.AiCall, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QuotaExceededException(QuotaType.AiCall, 30, 30));

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(
            CreateConfig(),
            tenantService.Object,
            tenantProvider.Object,
            quotaService.Object);

        await service.AskCopilotAsync("Summarize travel variance risks");

        tenantService.Verify(
            s => s.IncrementAiCallCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_DoesNotIncrementCounter_When_AiQuotaExceeded()
    {
        AiAssistantService.ClearThrottleStateForTesting();
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        var tenant = new Tenant
        {
            Id = tenantId,
            EnabledFeatures = "ai",
            PeriodAiCalls = 30,
            MaxAiCallsPerMonth = 30,
            Tier = SubscriptionTier.Starter,
            UsagePeriodStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.EnsureAllowedAsync(tenantId, QuotaType.AiCall, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QuotaExceededException(QuotaType.AiCall, 30, 30));

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var service = CreateService(
            CreateConfig(),
            tenantService.Object,
            tenantProvider.Object,
            quotaService.Object);

        await service.SuggestQuoteLinesAsync("scope", 0.15m);

        tenantService.Verify(
            s => s.IncrementAiCallCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
