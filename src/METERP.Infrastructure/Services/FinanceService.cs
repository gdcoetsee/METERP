using Microsoft.EntityFrameworkCore;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;
using METERP.Infrastructure.Caching;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class FinanceService : IFinanceService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantCacheService? _cache;

    public FinanceService(AppDbContext dbContext, ITenantCacheService? cache = null)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default)
    {
        if (_cache == null)
            return LoadAccountsAsync(ct);

        return _cache.GetOrCreateAsync(TenantCacheCategories.Finance, "accounts", () => LoadAccountsAsync(ct), ct: ct);
    }

    private async Task<IReadOnlyList<Account>> LoadAccountsAsync(CancellationToken ct)
    {
        return await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.AccountCode)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AccountBalanceRow>> GetAccountsWithBalancesAsync(CancellationToken ct = default)
    {
        if (_cache != null)
        {
            return await _cache.GetOrCreateAsync(TenantCacheCategories.Finance, "accounts-with-balances",
                () => LoadAccountsWithBalancesAsync(ct), ct: ct);
        }

        return await LoadAccountsWithBalancesAsync(ct);
    }

    private async Task<IReadOnlyList<AccountBalanceRow>> LoadAccountsWithBalancesAsync(CancellationToken ct)
    {
        var accounts = await LoadAccountsAsync(ct);
        if (accounts.Count == 0)
            return Array.Empty<AccountBalanceRow>();

        var accountIds = accounts.Select(a => a.Id).ToList();
        var aggregates = await _dbContext.Set<JournalEntryLine>()
            .AsNoTracking()
            .Where(l => accountIds.Contains(l.AccountId) && !l.IsDeleted)
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            })
            .ToListAsync(ct);

        var lookup = aggregates.ToDictionary(x => x.AccountId, x => x.Debit - x.Credit);
        var rows = new List<AccountBalanceRow>(accounts.Count);

        foreach (var account in accounts)
        {
            var raw = lookup.GetValueOrDefault(account.Id);
            var balance = raw;
            if (account.Type is AccountType.Liability or AccountType.Revenue or AccountType.Equity)
                balance = -raw;

            rows.Add(new AccountBalanceRow(account, balance));
        }

        return rows;
    }

    public async Task<Guid> CreateAccountAsync(Account account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.AccountCode))
            throw new InvalidOperationException("Account code is required.");
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException("Account name is required.");

        account.AccountCode = account.AccountCode.Trim();
        account.Name = account.Name.Trim();
        if (account.AccountCode.Length > 20)
            throw new InvalidOperationException("Account code cannot exceed 20 characters.");
        if (account.Name.Length > 200)
            throw new InvalidOperationException("Account name cannot exceed 200 characters.");

        var duplicate = await _dbContext.Set<Account>()
            .AnyAsync(a => a.AccountCode == account.AccountCode, ct);
        if (duplicate)
            throw new InvalidOperationException($"Account code '{account.AccountCode}' already exists.");

        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(ct);
        _cache?.InvalidateCategory(TenantCacheCategories.Finance);
        return account.Id;
    }

    public async Task SetAccountActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default)
    {
        var account = await _dbContext.Set<Account>().FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        account.IsActive = isActive;
        await _dbContext.SaveChangesAsync(ct);
        _cache?.InvalidateCategory(TenantCacheCategories.Finance);
    }

    public async Task<Guid> PostJournalAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.EntryNumber))
        {
            entry.EntryNumber = $"JE-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }
        else
        {
            entry.EntryNumber = entry.EntryNumber.Trim();
            var numberTaken = await _dbContext.Set<JournalEntry>()
                .AnyAsync(e => e.EntryNumber == entry.EntryNumber, ct);
            if (numberTaken)
                throw new InvalidOperationException(
                    $"Journal entry number '{entry.EntryNumber}' already exists.");
        }

        entry.EntryDate = entry.EntryDate == default ? DateTime.UtcNow.Date : entry.EntryDate.Date;
        if (entry.EntryDate > DateTime.UtcNow.Date.AddDays(1))
            throw new InvalidOperationException("Journal entry date cannot be more than one day in the future.");
        if (entry.EntryDate < DateTime.UtcNow.Date.AddYears(-10))
            throw new InvalidOperationException("Journal entry date cannot be more than 10 years in the past.");

        var lines = entry.Lines.Where(l => !l.IsDeleted).ToList();
        if (lines.Count < 2)
            throw new InvalidOperationException("Journal must have at least two lines.");

        if (lines.Any(l => l.Debit < 0 || l.Credit < 0))
            throw new InvalidOperationException("Journal line amounts cannot be negative.");

        if (lines.Any(l => l.Debit > 0 && l.Credit > 0))
            throw new InvalidOperationException("A journal line cannot have both debit and credit.");

        if (lines.Any(l => l.Debit == 0 && l.Credit == 0))
            throw new InvalidOperationException("Journal lines must have a debit or credit amount.");

        if (lines.Any(l => l.AccountId == Guid.Empty))
            throw new InvalidOperationException("Every journal line must reference an account.");

        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var existingAccounts = await _dbContext.Set<Account>()
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id) && a.IsActive)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (existingAccounts.Count != accountIds.Count)
            throw new InvalidOperationException("One or more journal accounts are missing or inactive.");

        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        if (Math.Abs(debits - credits) > 0.01m)
        {
            throw new InvalidOperationException("Journal does not balance (debits must equal credits).");
        }

        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            entry.Description = entry.Description.Trim();
            if (entry.Description.Length > 500)
                throw new InvalidOperationException("Journal description cannot exceed 500 characters.");
        }
        if (!string.IsNullOrWhiteSpace(entry.Reference))
        {
            entry.Reference = entry.Reference.Trim();
            if (entry.Reference.Length > 100)
                throw new InvalidOperationException("Journal reference cannot exceed 100 characters.");
        }

        foreach (var line in lines)
        {
            if (line.Debit > 100_000_000m || line.Credit > 100_000_000m)
                throw new InvalidOperationException("Journal line amount cannot exceed 100,000,000.");
            if (!string.IsNullOrWhiteSpace(line.Memo))
            {
                line.Memo = line.Memo.Trim();
                if (line.Memo.Length > 500)
                    throw new InvalidOperationException("Journal line memo cannot exceed 500 characters.");
            }
        }

        _dbContext.Set<JournalEntry>().Add(entry);
        await _dbContext.SaveChangesAsync(ct);
        _cache?.InvalidateCategory(TenantCacheCategories.Finance);
        return entry.Id;
    }

    public async Task<decimal> GetAccountBalanceAsync(Guid accountId, CancellationToken ct = default)
    {
        var lines = await _dbContext.Set<JournalEntryLine>()
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && !l.IsDeleted)
            .ToListAsync(ct);

        var account = await _dbContext.Set<Account>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        decimal balance = lines.Sum(l => l.Debit - l.Credit);

        if (account != null && (account.Type == AccountType.Liability || account.Type == AccountType.Revenue || account.Type == AccountType.Equity))
        {
            balance = -balance;
        }

        return balance;
    }

    public async Task<string> ExportGlCsvAsync(CancellationToken ct = default)
    {
        var entries = await _dbContext.Set<JournalEntry>()
            .AsNoTracking()
            .Include(e => e.Lines)
                .ThenInclude(l => l.Account)
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.EntryDate)
            .ThenBy(e => e.EntryNumber)
            .ToListAsync(ct);

        var exportLines = new List<GlJournalLineExport>();

        foreach (var entry in entries)
        {
            foreach (var line in entry.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Account?.AccountCode))
            {
                var account = line.Account;
                exportLines.Add(new GlJournalLineExport(
                    entry.EntryDate,
                    entry.EntryNumber,
                    entry.Reference,
                    account?.AccountCode ?? string.Empty,
                    account?.Name ?? string.Empty,
                    account?.Type.ToString() ?? string.Empty,
                    line.Debit,
                    line.Credit,
                    line.Memo,
                    entry.Description));
            }
        }

        return GlCsvExporter.BuildJournalLinesCsv(exportLines);
    }
}