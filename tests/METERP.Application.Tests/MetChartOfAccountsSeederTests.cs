using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using METERP.Application.Interfaces;
using METERP.Application.Models;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Seeding;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class MetChartOfAccountsSeederTests
{
    [Fact]
    public void StubAccounts_CoverThinSaContractorChart_WithoutJournals()
    {
        var accounts = MetChartOfAccountsSeeder.StubAccounts;

        Assert.InRange(accounts.Count, 12, 20);
        Assert.Contains(accounts, a => a.Code == "1000" && a.Type == AccountType.Asset && a.Name.Contains("Bank", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "1100" && a.Name.Contains("Debtors", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "1200" && a.Type == AccountType.Asset);
        Assert.Contains(accounts, a => a.Code == "2000" && a.Type == AccountType.Liability && a.Name.Contains("Creditors", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "2200" && a.Name.Contains("VAT", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("15%"));
        Assert.Contains(accounts, a => a.Code == "3000" && a.Type == AccountType.Equity);
        Assert.Contains(accounts, a => a.Code == "3100" && a.Type == AccountType.Equity);
        Assert.Contains(accounts, a => a.Code == "4000" && a.Type == AccountType.Revenue && a.Name.Contains("Contracting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "4100" && a.Name.Contains("Travel", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "5000" && a.Type == AccountType.Expense);
        Assert.Contains(accounts, a => a.Code == "5100" && a.Name.Contains("Labor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "5200" && a.Name.Contains("Travel", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(accounts, a => a.Code == "6000" && a.Name.Contains("Overhead", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Stub", MetChartOfAccountsSeeder.StubDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MET FY demo", MetChartOfAccountsSeeder.StubDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(accounts.Count, accounts.Select(a => a.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task EnsureForTenantIfEmpty_SeedsOnce_AndPostsNoJournals()
    {
        var tenantId = Guid.NewGuid();
        await using var harness = CreateHarness(tenantId);

        var created = await MetChartOfAccountsSeeder.EnsureForTenantIfEmptyAsync(
            harness.Db, harness.Finance, harness.TenantProvider, tenantId, NullLogger.Instance);

        Assert.Equal(MetChartOfAccountsSeeder.StubAccounts.Count, created);

        harness.TenantProvider.SetTenantId(tenantId);
        var accounts = await harness.Finance.GetAccountsAsync();
        Assert.Equal(created, accounts.Count);
        Assert.All(accounts, a => Assert.Equal(MetChartOfAccountsSeeder.StubDescription, a.Description));

        var again = await MetChartOfAccountsSeeder.EnsureForTenantIfEmptyAsync(
            harness.Db, harness.Finance, harness.TenantProvider, tenantId, NullLogger.Instance);
        Assert.Equal(0, again);
        Assert.Equal(created, await harness.Db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId));
        Assert.Equal(0, await harness.Db.Set<JournalEntry>().IgnoreQueryFilters().CountAsync(j => j.TenantId == tenantId));
        Assert.Equal(0, await harness.Db.Set<JournalEntryLine>().IgnoreQueryFilters().CountAsync(l => l.TenantId == tenantId));
    }

    [Fact]
    public async Task EnsureForTenantIfEmpty_SkipsWhenAnyAccountExists()
    {
        var tenantId = Guid.NewGuid();
        await using var harness = CreateHarness(tenantId);
        harness.TenantProvider.SetTenantId(tenantId);
        await harness.Finance.CreateAccountAsync(new Account
        {
            AccountCode = "9999",
            Name = "Existing",
            Type = AccountType.Asset
        });

        var created = await MetChartOfAccountsSeeder.EnsureForTenantIfEmptyAsync(
            harness.Db, harness.Finance, harness.TenantProvider, tenantId, NullLogger.Instance);

        Assert.Equal(0, created);
        Assert.Equal(1, await harness.Db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId && !a.IsDeleted));
    }

    [Fact]
    public async Task EnsureForMetOfficeTenants_SeedsOnlyMetElectrical()
    {
        var metId = Guid.NewGuid();
        var acmeId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        await using var harness = CreateHarness(metId);

        harness.Db.Set<Tenant>().AddRange(
            new Tenant { Id = metId, TenantId = metId, Name = "MET Electrical", Subdomain = "acme", BrandDisplayName = "MET Electrical" },
            new Tenant { Id = acmeId, TenantId = acmeId, Name = "Acme Electrical (Demo)", Subdomain = "acme-e2e", BrandDisplayName = "Acme Electrical" },
            new Tenant { Id = betaId, TenantId = betaId, Name = "Beta Corp (Demo)", Subdomain = "beta" });
        await harness.Db.SaveChangesAsync();

        var created = await MetChartOfAccountsSeeder.EnsureForMetOfficeTenantsAsync(
            harness.Db, harness.Finance, harness.TenantProvider, NullLogger.Instance);

        Assert.Equal(MetChartOfAccountsSeeder.StubAccounts.Count, created);
        Assert.Equal(created, await harness.Db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == metId && !a.IsDeleted));
        Assert.Equal(0, await harness.Db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == acmeId || a.TenantId == betaId));
        Assert.True(TenantBranding.IsMetOfficeTenant(await harness.Db.Set<Tenant>().FirstAsync(t => t.Id == metId)));
    }

    private static Harness CreateHarness(Guid tenantId)
    {
        var tenantProvider = new CurrentTenantProvider();
        tenantProvider.SetTenantId(tenantId);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.UserName).Returns("seed-test");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantProvider, currentUser.Object);
        return new Harness(db, new FinanceService(db), tenantProvider);
    }

    private sealed class Harness(AppDbContext db, FinanceService finance, CurrentTenantProvider tenantProvider) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public FinanceService Finance { get; } = finance;
        public CurrentTenantProvider TenantProvider { get; } = tenantProvider;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
