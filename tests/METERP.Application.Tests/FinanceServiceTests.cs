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
}