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
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(ct);
        _cache?.InvalidateCategory(TenantCacheCategories.Finance);
        return account.Id;
    }

    public async Task<Guid> PostJournalAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.EntryNumber))
        {
            entry.EntryNumber = $"JE-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        var debits = entry.Lines.Sum(l => l.Debit);
        var credits = entry.Lines.Sum(l => l.Credit);
        if (Math.Abs(debits - credits) > 0.01m)
        {
            throw new InvalidOperationException("Journal does not balance (debits must equal credits).");
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