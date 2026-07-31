using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Persistence;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class FinanceServiceTests
{
    private AppDbContext CreateInMemoryContext(Guid tenantId)
    {
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(s => s.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, tenantProviderMock.Object, currentUserMock.Object);
    }

    [Fact]
    public async Task ExportGlCsvAsync_ExportsJournalLinesWithAccountCodes()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);

        var revenue = new Account
        {
            TenantId = tenantId,
            AccountCode = "4000",
            Name = "Revenue",
            Type = AccountType.Revenue
        };
        var ar = new Account
        {
            TenantId = tenantId,
            AccountCode = "1100",
            Name = "Accounts Receivable",
            Type = AccountType.Asset
        };
        db.Set<Account>().AddRange(revenue, ar);
        await db.SaveChangesAsync();

        var entry = new JournalEntry
        {
            TenantId = tenantId,
            EntryNumber = "JE-TEST-001",
            EntryDate = new DateTime(2026, 6, 1),
            Description = "Test revenue",
            Reference = "INV-TEST",
            Lines = new List<JournalEntryLine>
            {
                new()
                {
                    TenantId = tenantId,
                    AccountId = ar.Id,
                    Debit = 1000m
                },
                new()
                {
                    TenantId = tenantId,
                    AccountId = revenue.Id,
                    Credit = 1000m
                }
            }
        };
        db.Set<JournalEntry>().Add(entry);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var csv = await service.ExportGlCsvAsync();

        Assert.StartsWith(GlCsvExporter.Header, csv);
        Assert.Contains("JE-TEST-001", csv);
        Assert.Contains("4000", csv);
        Assert.Contains("1100", csv);
        Assert.Contains("1000.00", csv);
    }

    [Fact]
    public async Task GetAccountsWithBalancesAsync_ReturnsSignedBalancesPerAccountType()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);

        var revenue = new Account
        {
            TenantId = tenantId,
            AccountCode = "4000",
            Name = "Revenue",
            Type = AccountType.Revenue
        };
        var expense = new Account
        {
            TenantId = tenantId,
            AccountCode = "5000",
            Name = "Materials",
            Type = AccountType.Expense
        };
        db.Set<Account>().AddRange(revenue, expense);
        await db.SaveChangesAsync();

        db.Set<JournalEntryLine>().AddRange(
            new JournalEntryLine
            {
                TenantId = tenantId,
                AccountId = revenue.Id,
                Credit = 2000m,
                JournalEntry = new JournalEntry { TenantId = tenantId, EntryNumber = "JE-BAL-1" }
            },
            new JournalEntryLine
            {
                TenantId = tenantId,
                AccountId = expense.Id,
                Debit = 500m,
                JournalEntry = new JournalEntry { TenantId = tenantId, EntryNumber = "JE-BAL-2" }
            });
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var rows = await service.GetAccountsWithBalancesAsync();

        var revenueRow = rows.First(r => r.Account.AccountCode == "4000");
        var expenseRow = rows.First(r => r.Account.AccountCode == "5000");

        Assert.Equal(2000m, revenueRow.Balance);
        Assert.Equal(500m, expenseRow.Balance);
    }

    [Fact]
    public async Task CreateAccountAsync_ThrowsWhenCodeTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAccountAsync(new Account
            {
                TenantId = tenantId,
                AccountCode = new string('9', 21),
                Name = "Too long code",
                Type = AccountType.Asset
            }));
        Assert.Contains("20 characters", ex.Message);
    }

    [Fact]
    public async Task PostJournalAsync_Throws_When_Unbalanced()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        db.Set<Account>().Add(cash);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var entry = new JournalEntry
        {
            TenantId = tenantId,
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 100m },
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Credit = 50m }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostJournalAsync(entry));
    }

    [Fact]
    public async Task PostJournalAsync_Throws_WhenAccountMissingOrInactive()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var inactive = new Account
        {
            TenantId = tenantId,
            AccountCode = "9999",
            Name = "Closed",
            Type = AccountType.Asset,
            IsActive = false
        };
        db.Set<Account>().AddRange(cash, inactive);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var entry = new JournalEntry
        {
            TenantId = tenantId,
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 100m },
                new JournalEntryLine { TenantId = tenantId, AccountId = inactive.Id, Credit = 100m }
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostJournalAsync(entry));
        Assert.Contains("account", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostJournalAsync_AssignsEntryNumber_WhenMissing()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var entry = new JournalEntry
        {
            TenantId = tenantId,
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 250m },
                new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 250m }
            }
        };

        var id = await service.PostJournalAsync(entry);

        Assert.NotEqual(Guid.Empty, id);
        Assert.StartsWith("JE-", entry.EntryNumber);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenEntryNumberDuplicate()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        await service.PostJournalAsync(new JournalEntry
        {
            TenantId = tenantId,
            EntryNumber = "JE-DUP-1",
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
            }
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                EntryNumber = "JE-DUP-1",
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 5m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 5m }
                }
            }));
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenEntryDateTooFarFuture()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                EntryDate = DateTime.UtcNow.Date.AddDays(14),
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
                }
            }));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenReferenceTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                Reference = new string('R', 101),
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
                }
            }));
        Assert.Contains("100 characters", ex.Message);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenEntryNumberTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                EntryNumber = new string('J', 51),
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
                }
            }));
        Assert.Contains("50 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAccountAsync_ThrowsWhenAccountCodeTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAccountAsync(new Account
            {
                TenantId = tenantId,
                AccountCode = new string('1', 21),
                Name = "Long code",
                Type = AccountType.Asset
            }));
        Assert.Contains("20 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAccountAsync_AcceptsAccountCodeAt20Characters()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var id = await service.CreateAccountAsync(new Account
        {
            TenantId = tenantId,
            AccountCode = new string('1', 20),
            Name = "Code ok",
            Type = AccountType.Asset
        });
        var saved = await db.Set<Account>().FirstAsync(a => a.Id == id);
        Assert.Equal(20, saved.AccountCode.Length);
    }

    [Fact]
    public async Task CreateAccountAsync_ThrowsWhenNameTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAccountAsync(new Account
            {
                TenantId = tenantId,
                AccountCode = "1999",
                Name = new string('N', 201),
                Type = AccountType.Asset
            }));
        Assert.Contains("200 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAccountAsync_AcceptsNameAt200Characters()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var id = await service.CreateAccountAsync(new Account
        {
            TenantId = tenantId,
            AccountCode = "1998",
            Name = new string('N', 200),
            Type = AccountType.Asset
        });
        var saved = await db.Set<Account>().FirstAsync(a => a.Id == id);
        Assert.Equal(200, saved.Name.Length);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenDescriptionTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                Description = new string('D', 501),
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
                }
            }));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task PostJournalAsync_AcceptsDescriptionAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var id = await service.PostJournalAsync(new JournalEntry
        {
            TenantId = tenantId,
            Description = new string('D', 500),
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
            }
        });
        var saved = await db.Set<JournalEntry>().FirstAsync(e => e.Id == id);
        Assert.Equal(500, saved.Description!.Length);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenLineMemoTooLong()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m, Memo = new string('M', 501) },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
                }
            }));
        Assert.Contains("500 characters", ex.Message);
    }

    [Fact]
    public async Task PostJournalAsync_AcceptsLineMemoAt500Characters()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var id = await service.PostJournalAsync(new JournalEntry
        {
            TenantId = tenantId,
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m, Memo = new string('M', 500) },
                new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
            }
        });
        var line = await db.Set<JournalEntryLine>().FirstAsync(l => l.JournalEntryId == id && l.Debit > 0);
        Assert.Equal(500, line.Memo!.Length);
    }

    [Fact]
    public async Task PostJournalAsync_AcceptsReferenceAt100Characters()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var id = await service.PostJournalAsync(new JournalEntry
        {
            TenantId = tenantId,
            Reference = new string('R', 100),
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 10m },
                new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 10m }
            }
        });
        var saved = await db.Set<JournalEntry>().FirstAsync(e => e.Id == id);
        Assert.Equal(100, saved.Reference!.Length);
    }

    [Fact]
    public async Task PostJournalAsync_ThrowsWhenLineAmountTooHigh()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        var revenue = new Account { TenantId = tenantId, AccountCode = "4000", Name = "Revenue", Type = AccountType.Revenue };
        db.Set<Account>().AddRange(cash, revenue);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostJournalAsync(new JournalEntry
            {
                TenantId = tenantId,
                Lines =
                {
                    new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 100_000_001m },
                    new JournalEntryLine { TenantId = tenantId, AccountId = revenue.Id, Credit = 100_000_001m }
                }
            }));
        Assert.Contains("100,000,000", ex.Message);
    }

    [Fact]
    public async Task GetAccountBalanceAsync_ReturnsNetDebitMinusCredit_ForAssetAccount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var cash = new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset };
        db.Set<Account>().Add(cash);
        await db.SaveChangesAsync();

        var entry = new JournalEntry
        {
            TenantId = tenantId,
            EntryNumber = "JE-BAL",
            Lines =
            {
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Debit = 300m },
                new JournalEntryLine { TenantId = tenantId, AccountId = cash.Id, Credit = 100m }
            }
        };
        db.Set<JournalEntry>().Add(entry);
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var balance = await service.GetAccountBalanceAsync(cash.Id);

        Assert.Equal(200m, balance);
    }

    [Fact]
    public async Task GetAccountsAsync_ExcludesInactiveAccounts()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        db.Set<Account>().AddRange(
            new Account { TenantId = tenantId, AccountCode = "1000", Name = "Cash", Type = AccountType.Asset, IsActive = true },
            new Account { TenantId = tenantId, AccountCode = "9999", Name = "Legacy", Type = AccountType.Asset, IsActive = false });
        await db.SaveChangesAsync();

        var service = new FinanceService(db);
        var accounts = await service.GetAccountsAsync();

        Assert.Single(accounts);
        Assert.Equal("1000", accounts[0].AccountCode);
    }

    [Fact]
    public async Task SetAccountActiveAsync_DeactivatesAccount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);
        var id = await service.CreateAccountAsync(new Account
        {
            TenantId = tenantId,
            AccountCode = "1500",
            Name = "Temp asset",
            Type = AccountType.Asset
        });

        await service.SetAccountActiveAsync(id, false);

        var accounts = await service.GetAccountsAsync();
        Assert.DoesNotContain(accounts, a => a.Id == id);
        var raw = await db.Set<Account>().FirstAsync(a => a.Id == id);
        Assert.False(raw.IsActive);
    }

    [Fact]
    public async Task CreateAccountAsync_PersistsActiveAccount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateInMemoryContext(tenantId);
        var service = new FinanceService(db);

        var id = await service.CreateAccountAsync(new Account
        {
            TenantId = tenantId,
            AccountCode = "2100",
            Name = "Accounts Payable",
            Type = AccountType.Liability
        });

        var accounts = await service.GetAccountsAsync();
        var created = Assert.Single(accounts);
        Assert.Equal(id, created.Id);
        Assert.Equal("2100", created.AccountCode);
        Assert.Equal(AccountType.Liability, created.Type);
    }
}