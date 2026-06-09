using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service for Quote management and the Quote -> Job conversion.
/// </summary>
public interface IQuoteService
{
    Task<Quote?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> GetAllAsync(string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default);

    Task<Guid> CreateAsync(Quote quote, CancellationToken ct = default);
    Task UpdateAsync(Quote quote, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Line item management (inline like Contacts on Customer)
    Task<Guid> AddLineAsync(QuoteLine line, CancellationToken ct = default);
    Task UpdateLineAsync(QuoteLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Converts an accepted quote into a new Job, snapshots totals, and returns the created Job.
    /// Also updates the quote status if needed.
    /// </summary>
    Task<Job> ConvertToJobAsync(Guid quoteId, CancellationToken ct = default);
}
