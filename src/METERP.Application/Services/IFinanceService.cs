using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Minimal finance service for GL, journals, and basic financial visibility to support costing/invoicing.
/// </summary>
public interface IFinanceService
{
    Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken ct = default);
    Task<Guid> CreateAccountAsync(Account account, CancellationToken ct = default);

    Task<Guid> PostJournalAsync(JournalEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Simple account balance (debits - credits or as per type).
    /// </summary>
    Task<decimal> GetAccountBalanceAsync(Guid accountId, CancellationToken ct = default);
}
