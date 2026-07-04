using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class TenantAiSettingsServiceTests
{
    private sealed class Harness : IDisposable
    {
        public Guid TenantId { get; }
        public AppDbContext Db { get; }
        public Mock<ITenantProvider> TenantProvider { get; }
        public TenantService TenantService { get; }
        public TenantAiSettingsService AiSettingsService { get; }
        private readonly IDataProtectionProvider _dataProtection;

        public Harness(Guid tenantId)
        {
            TenantId = tenantId;
            var dbName = Guid.NewGuid().ToString();

            TenantProvider = new Mock<ITenantProvider>();
            TenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
            currentUser.Setup(u => u.UserName).Returns("ai-settings-test");

            var services = new ServiceCollection();
            services.AddScoped(_ => TenantProvider.Object);
            services.AddScoped(_ => currentUser.Object);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            Db = provider.GetRequiredService<AppDbContext>();
            TenantService = new TenantService(Db, scopeFactory);
            _dataProtection = new EphemeralDataProtectionProvider();
            AiSettingsService = new TenantAiSettingsService(
                TenantService,
                TenantProvider.Object,
                _dataProtection,
                NullLogger<TenantAiSettingsService>.Instance);
        }

        public async Task SeedTenantAsync(Action<Tenant>? configure = null)
        {
            var tenant = new Tenant
            {
                Id = TenantId,
                TenantId = TenantId,
                Name = "AI Tenant",
                Subdomain = $"ai-{TenantId:N}".Substring(0, 12),
                Tier = SubscriptionTier.Starter,
                EnabledFeatures = "ai"
            };
            configure?.Invoke(tenant);
            Db.Tenants.Add(tenant);
            await Db.SaveChangesAsync();
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task GetCurrentTenantSettingsAsync_ReturnsOpenAiDefaults_WhenTenantHasNoOverrides()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();

        var settings = await harness.AiSettingsService.GetCurrentTenantSettingsAsync();

        Assert.Equal(AiProviderProfiles.OpenAi, settings.Provider);
        Assert.Equal("https://api.openai.com/v1", settings.BaseUrl);
        Assert.Equal("gpt-4o-mini", settings.Model);
        Assert.False(settings.UseTenantKey);
        Assert.False(settings.HasStoredKey);
        Assert.Equal("(not set)", settings.MaskedApiKey);
    }

    [Fact]
    public async Task SaveCurrentTenantSettingsAsync_PersistsProviderModelAndEncryptedKey()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();

        await harness.AiSettingsService.SaveCurrentTenantSettingsAsync(
            AiProviderProfiles.Groq,
            "https://api.groq.com/openai/v1",
            "llama-3.3-70b-versatile",
            useTenantKey: true,
            apiKey: "gsk_test_secret_key_12345678");

        var settings = await harness.AiSettingsService.GetCurrentTenantSettingsAsync();
        Assert.Equal(AiProviderProfiles.Groq, settings.Provider);
        Assert.Equal("https://api.groq.com/openai/v1", settings.BaseUrl);
        Assert.Equal("llama-3.3-70b-versatile", settings.Model);
        Assert.True(settings.UseTenantKey);
        Assert.True(settings.HasStoredKey);
        Assert.StartsWith("gsk_", settings.MaskedApiKey);
        Assert.Contains("••••", settings.MaskedApiKey);
    }

    [Fact]
    public async Task SaveCurrentTenantSettingsAsync_WithoutApiKey_PreservesExistingEncryptedKey()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();

        await harness.AiSettingsService.SaveCurrentTenantSettingsAsync(
            AiProviderProfiles.OpenAi,
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            useTenantKey: true,
            apiKey: "sk-existing-key-abcdef12");

        await harness.AiSettingsService.SaveCurrentTenantSettingsAsync(
            AiProviderProfiles.Groq,
            "https://api.groq.com/openai/v1",
            "llama-3.3-70b-versatile",
            useTenantKey: true,
            apiKey: null);

        var settings = await harness.AiSettingsService.GetCurrentTenantSettingsAsync();
        Assert.Equal(AiProviderProfiles.Groq, settings.Provider);
        Assert.True(settings.HasStoredKey);
        Assert.StartsWith("sk-e", settings.MaskedApiKey);
    }

    [Fact]
    public async Task GetCurrentTenantSettingsAsync_IsTenantIsolated()
    {
        var acmeId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        using var acmeHarness = new Harness(acmeId);
        await acmeHarness.SeedTenantAsync();
        await acmeHarness.AiSettingsService.SaveCurrentTenantSettingsAsync(
            AiProviderProfiles.Groq,
            "https://api.groq.com/openai/v1",
            "llama-3.3-70b-versatile",
            useTenantKey: true,
            apiKey: "gsk_acme_only_key_12345678");

        using var betaHarness = new Harness(betaId);
        await betaHarness.SeedTenantAsync();

        var betaSettings = await betaHarness.AiSettingsService.GetCurrentTenantSettingsAsync();

        Assert.Equal(AiProviderProfiles.OpenAi, betaSettings.Provider);
        Assert.False(betaSettings.HasStoredKey);
    }

    [Fact]
    public async Task GetCurrentTenantSettingsAsync_Throws_WhenNoTenantContext()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();
        harness.TenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.AiSettingsService.GetCurrentTenantSettingsAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_WhenNoApiKeyAvailable()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();

        var result = await harness.AiSettingsService.TestConnectionAsync(
            AiProviderProfiles.OpenAi,
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            apiKey: null);

        Assert.False(result.Success);
        Assert.Contains("API key is required", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFailure_ForInvalidGoogleGeminiKeyFormat()
    {
        var tenantId = Guid.NewGuid();
        using var harness = new Harness(tenantId);
        await harness.SeedTenantAsync();

        var result = await harness.AiSettingsService.TestConnectionAsync(
            AiProviderProfiles.GoogleGemini,
            "https://generativelanguage.googleapis.com/v1beta/openai",
            "gemini-flash-latest",
            apiKey: "sk-not-a-google-key");

        Assert.False(result.Success);
        Assert.Contains("AIza", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}