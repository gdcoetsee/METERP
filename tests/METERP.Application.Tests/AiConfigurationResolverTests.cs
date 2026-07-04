using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AiConfigurationResolverTests
{
    private static IConfiguration CreateDeploymentConfig(string apiKey = "deploy-sk-key")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ai:ApiKey"] = apiKey,
            ["Ai:BaseUrl"] = "https://api.openai.com/v1",
            ["Ai:Model"] = "gpt-4o-mini",
            ["Ai:Enabled"] = "true",
            ["Ai:Provider"] = AiProviderProfiles.OpenAi,
            ["Ai:TimeoutSeconds"] = "45"
        };

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static (AppDbContext Db, TenantService TenantService, Mock<ITenantProvider> TenantProvider) CreateDbHarness(Guid tenantId)
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns(Guid.NewGuid());
        currentUser.Setup(u => u.UserName).Returns("ai-resolver-test");

        var services = new ServiceCollection();
        services.AddScoped(_ => tenantProvider.Object);
        services.AddScoped(_ => currentUser.Object);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        var tenantService = new TenantService(db, provider.GetRequiredService<IServiceScopeFactory>());
        return (db, tenantService, tenantProvider);
    }

    [Fact]
    public async Task GetEffectiveAsync_ReturnsDeploymentConfig_WhenTenantOverrideDisabled()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantService, tenantProvider) = CreateDbHarness(tenantId);
        await using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                AiUseTenantKey = false
            });
            await db.SaveChangesAsync();

            var resolver = new AiConfigurationResolver(
                CreateDeploymentConfig(),
                tenantProvider.Object,
                tenantService);

            var config = await resolver.GetEffectiveAsync();

            Assert.False(config.FromTenantOverride);
            Assert.Equal("deploy-sk-key", config.ApiKey);
            Assert.Equal("https://api.openai.com/v1", config.BaseUrl);
            Assert.Equal("gpt-4o-mini", config.Model);
            Assert.Equal(45, config.TimeoutSeconds);
        }
    }

    [Fact]
    public async Task GetEffectiveAsync_ReturnsTenantOverride_WhenUseTenantKeyAndStoredKey()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantService, tenantProvider) = CreateDbHarness(tenantId);
        var dataProtection = new EphemeralDataProtectionProvider();
        var protector = dataProtection.CreateProtector("METERP.TenantAiSettings");

        await using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                AiProvider = AiProviderProfiles.Groq,
                AiBaseUrl = "https://api.groq.com/openai/v1",
                AiModel = "llama-3.3-70b-versatile",
                AiUseTenantKey = true,
                AiApiKeyEncrypted = protector.Protect("gsk_tenant_override_key")
            });
            await db.SaveChangesAsync();

            var resolver = new AiConfigurationResolver(
                CreateDeploymentConfig(),
                tenantProvider.Object,
                tenantService,
                dataProtection);

            var config = await resolver.GetEffectiveAsync();

            Assert.True(config.FromTenantOverride);
            Assert.Equal("gsk_tenant_override_key", config.ApiKey);
            Assert.Equal("https://api.groq.com/openai/v1", config.BaseUrl);
            Assert.Equal("llama-3.3-70b-versatile", config.Model);
            Assert.Equal(AiProviderProfiles.Groq, config.ProviderName);
        }
    }

    [Fact]
    public async Task GetEffectiveAsync_FallsBackToDeployment_WhenTenantKeyCannotBeDecrypted()
    {
        var tenantId = Guid.NewGuid();
        var (db, tenantService, tenantProvider) = CreateDbHarness(tenantId);

        await using (db)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                TenantId = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                AiUseTenantKey = true,
                AiApiKeyEncrypted = "corrupted-ciphertext"
            });
            await db.SaveChangesAsync();

            var resolver = new AiConfigurationResolver(
                CreateDeploymentConfig("deploy-fallback-key"),
                tenantProvider.Object,
                tenantService,
                new EphemeralDataProtectionProvider());

            var config = await resolver.GetEffectiveAsync();

            Assert.False(config.FromTenantOverride);
            Assert.Equal("deploy-fallback-key", config.ApiKey);
        }
    }

    [Fact]
    public void IsDeploymentConfigured_True_WhenApiKeyPresent()
    {
        var resolver = new AiConfigurationResolver(CreateDeploymentConfig());
        Assert.True(resolver.IsDeploymentConfigured);
    }

    [Fact]
    public void IsDeploymentConfigured_False_WhenApiKeyMissing()
    {
        var resolver = new AiConfigurationResolver(CreateDeploymentConfig(apiKey: ""));
        Assert.False(resolver.IsDeploymentConfigured);
    }
}