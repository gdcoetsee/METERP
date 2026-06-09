using Microsoft.EntityFrameworkCore;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Persistence;

namespace METERP.Infrastructure.Services;

public class FinanceService : IFinanceService
{
    private readonly AppDbContext _dbContext;

    public FinanceService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default)
    {
        return await _dbContext.Set<Account>()
            .Where(a => a.IsActive)
            .OrderBy(a => a.AccountCode)
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateAccountAsync(Account account, CancellationToken ct = default)
    {
        _dbContext.Set<Account>().Add(account);
        await _dbContext.SaveChangesAsync(ct);
        return account.Id;
    }

    public async Task<Guid> PostJournalAsync(JournalEntry entry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entry.EntryNumber))
        {
            entry.EntryNumber = $"JE-{DateTime.UtcNow.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
        }

        // Basic balance check (debits == credits)
        var debits = entry.Lines.Sum(l => l.Debit);
        var credits = entry.Lines.Sum(l => l.Credit);
        if (Math.Abs(debits - credits) > 0.01m)
        {
            throw new InvalidOperationException("Journal does not balance (debits must equal credits).");
        }

        _dbContext.Set<JournalEntry>().Add(entry);
        await _dbContext.SaveChangesAsync(ct);
        return entry.Id;
    }

    public async Task<decimal> GetAccountBalanceAsync(Guid accountId, CancellationToken ct = default)
    {
        var lines = await _dbContext.Set<JournalEntryLine>()
            .Where(l => l.AccountId == accountId)
            .ToListAsync(ct);

        // Simple: for Expense/Asset positive = debit balance; adjust as needed for full accounting.
        var account = await _dbContext.Set<Account>().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        decimal balance = lines.Sum(l => l.Debit - l.Credit);

        // Rough sign flip for liability/revenue (common simple view)
        if (account != null && (account.Type == AccountType.Liability || account.Type == AccountType.Revenue || account.Type == AccountType.Equity))
        {
            balance = -balance;
        }

        return balance;
    }
}
