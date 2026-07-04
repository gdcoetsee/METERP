using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using METERP.Application.Interfaces;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Verifies FinanceService GL list caching via ITenantCacheService (Phase 5 distributed cache maturity).
/// </summary>
public class FinanceServiceCacheTests
{
    private static (AppDbContext Db, TenantDistributedCacheService Cache, FinanceService Service) CreateHarness(Guid tenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenantProvider.Object, currentUser.Object);

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.Configure<CacheOptions>(o => o.DefaultTtlSeconds = 120);
        var provider = services.BuildServiceProvider();
        var cache = new TenantDistributedCacheService(
            provider.GetRequiredService<IDistributedCache>(),
            tenantProvider.Object,
            provider.GetRequiredService<IOptions<CacheOptions>>());

        return (db, cache, new FinanceService(db, cache));
    }

    [Fact]
    public async Task GetAccountsAsync_ReturnsCachedListUntilCategoryInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            db.Set<Account>().Add(new Account
            {
                TenantId = tenantId,
                AccountCode = "1000",
                Name = "Cash",
                Type = AccountType.Asset
            });
            await db.SaveChangesAsync();

            Assert.Equal("Cash", (await service.GetAccountsAsync())[0].Name);

            (await db.Set<Account>().FirstAsync()).Name = "Cash Updated";
            await db.SaveChangesAsync();
            Assert.Equal("Cash", (await service.GetAccountsAsync())[0].Name);

            cache.InvalidateCategory(TenantCacheCategories.Finance);
            Assert.Equal("Cash Updated", (await service.GetAccountsAsync())[0].Name);
        }
    }

    [Fact]
    public async Task CreateAccountAsync_InvalidatesFinanceListCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            await service.CreateAccountAsync(new Account
            {
                TenantId = tenantId,
                AccountCode = "1000",
                Name = "Cash",
                Type = AccountType.Asset
            });
            Assert.Single(await service.GetAccountsAsync());

            await service.CreateAccountAsync(new Account
            {
                TenantId = tenantId,
                AccountCode = "2000",
                Name = "Payables",
                Type = AccountType.Liability
            });

            var accounts = await service.GetAccountsAsync();
            Assert.Equal(2, accounts.Count);
            Assert.Contains(accounts, a => a.AccountCode == "2000");
        }
    }

    [Fact]
    public async Task GetAccountsWithBalancesAsync_ReturnsCachedBalancesUntilInvalidated()
    {
        var tenantId = Guid.NewGuid();
        var (db, cache, service) = CreateHarness(tenantId);
        using (db)
        {
            var cash = new Account
            {
                TenantId = tenantId,
                AccountCode = "1000",
                Name = "Cash",
                Type = AccountType.Asset
            };
            db.Set<Account>().Add(cash);
            await db.SaveChangesAsync();

            db.Set<JournalEntryLine>().Add(new JournalEntryLine
            {
                TenantId = tenantId,
                AccountId = cash.Id,
                Debit = 100m,
                JournalEntry = new JournalEntry { TenantId = tenantId, EntryNumber = "JE-1" }
            });
            await db.SaveChangesAsync();

            Assert.Equal(100m, (await service.GetAccountsWithBalancesAsync())[0].Balance);

            var line = await db.Set<JournalEntryLine>().FirstAsync();
            line.Debit = 250m;
            await db.SaveChangesAsync();
            Assert.Equal(100m, (await service.GetAccountsWithBalancesAsync())[0].Balance);

            cache.InvalidateCategory(TenantCacheCategories.Finance);
            Assert.Equal(250m, (await service.GetAccountsWithBalancesAsync())[0].Balance);
        }
    }

    [Fact]
    public async Task PostJournalAsync_InvalidatesFinanceBalanceCache()
    {
        var tenantId = Guid.NewGuid();
        var (db, _, service) = CreateHarness(tenantId);
        using (db)
        {
            var cash = new Account
            {
                TenantId = tenantId,
                AccountCode = "1000",
                Name = "Cash",
                Type = AccountType.Asset
            };
            var revenue = new Account
            {
                TenantId = tenantId,
                AccountCode = "4000",
                Name = "Revenue",
                Type = AccountType.Revenue
            };
            db.Set<Account>().AddRange(cash, revenue);
            await db.SaveChangesAsync();

            Assert.Equal(0m, (await service.GetAccountsWithBalancesAsync()).First(r => r.Account.AccountCode == "1000").Balance);

            await service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 500m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 500m }
                }
            });

            var cashRow = (await service.GetAccountsWithBalancesAsync()).First(r => r.Account.AccountCode == "1000");
            Assert.Equal(500m, cashRow.Balance);
        }
    }
}