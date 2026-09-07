using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using METERP.Application.Interfaces;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class E2EDemoQuotaSeederTests
{
    [Fact]
    public async Task EnsureQuoteQuotaExceededAsync_SetsLimitAndUsedViaExecuteUpdate()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton(new Mock<ITenantProvider>().Object);
        services.AddSingleton(new Mock<ICurrentUserService>().Object);
        await using var provider = services.BuildServiceProvider();

        Guid tenantId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                MaxQuotesPerMonth = E2EDemoQuotaSeeder.DemoMonthlyLimit,
                PeriodQuotesCreated = 0
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await E2EDemoQuotaSeeder.EnsureQuoteQuotaExceededAsync(db, tenantId);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
            Assert.Equal(1, tenant.MaxQuotesPerMonth);
            Assert.Equal(1, tenant.PeriodQuotesCreated);
        }
    }

    [Fact]
    public async Task ResetDemoQuotasAsync_RestoresDemoLimits()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton(new Mock<ITenantProvider>().Object);
        services.AddSingleton(new Mock<ICurrentUserService>().Object);
        await using var provider = services.BuildServiceProvider();

        Guid tenantId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            tenantId = Guid.NewGuid();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Acme",
                Subdomain = "acme",
                MaxQuotesPerMonth = 1,
                PeriodQuotesCreated = 1
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await E2EDemoQuotaSeeder.ResetDemoQuotasAsync(db, tenantId);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
            Assert.Equal(E2EDemoQuotaSeeder.DemoMonthlyLimit, tenant.MaxQuotesPerMonth);
            Assert.Equal(0, tenant.PeriodQuotesCreated);
        }
    }
}
