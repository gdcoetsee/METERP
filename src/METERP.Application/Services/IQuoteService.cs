using METERP.Application.Models;
using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Application service for Quote management and the Quote -> Job conversion.
/// </summary>
public interface IQuoteService
{
    Task<Quote?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Quote>> GetAllAsync(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default,
        QuoteBoardFilter filter = QuoteBoardFilter.All,
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null);

    /// <summary>Count quotes in the same search, board, and register window as <see cref="GetAllAsync"/>.</summary>
    Task<int> CountAsync(
        string? search = null,
        QuoteBoardFilter filter = QuoteBoardFilter.All,
        CancellationToken ct = default,
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null);

    /// <summary>
    /// Quote book for a date window and status. From and to are inclusive calendar days.
    /// VAT on each row is the stored tax. A job in another tenant does not count.
    /// </summary>
    Task<IReadOnlyList<QuoteRegisterRow>> GetRegisterAsync(
        DateTime? from = null,
        DateTime? to = null,
        QuoteStatus? status = null,
        CancellationToken ct = default);

    /// <summary>Quote ids in this tenant that have a non-deleted job. Other tenants' jobs are ignored.</summary>
    Task<IReadOnlySet<Guid>> GetQuoteIdsWithJobsAsync(
        IReadOnlyCollection<Guid> quoteIds,
        CancellationToken ct = default);

    Task<Guid> CreateAsync(Quote quote, CancellationToken ct = default);

    /// <summary>
    /// One-click quote from a deal. Travel mentioned in the title or notes is an explicit Travel line, not a materials lump.
    /// </summary>
    Task<Quote> CreateQuoteFromOpportunityAsync(Guid opportunityId, CancellationToken ct = default);
    Task UpdateAsync(Quote quote, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SubmitForExecutiveApprovalAsync(Guid quoteId, Guid submittedByUserId, CancellationToken ct = default);

    Task ExecutiveApproveAsync(Guid quoteId, Guid approverUserId, string? note = null, CancellationToken ct = default);

    Task ExecutiveRejectAsync(Guid quoteId, Guid approverUserId, string reason, CancellationToken ct = default);

    /// <summary>Estimator withdraws a pending quote from the executive queue so lines can be edited again.</summary>
    Task WithdrawFromApprovalAsync(Guid quoteId, Guid userId, string? reason = null, CancellationToken ct = default);

    Task<IReadOnlyList<Quote>> GetPendingExecutiveApprovalAsync(CancellationToken ct = default);

    /// <summary>Sent or accepted quotes that have not been converted to a job.</summary>
    Task<IReadOnlyList<ConvertibleDocumentRow>> GetUnconvertedWonQuotesAsync(int take = 20, CancellationToken ct = default);

    /// <summary>Executive-approved draft quotes that still need to be sent to the customer.</summary>
    Task<IReadOnlyList<ConvertibleDocumentRow>> GetApprovedUnsentQueueAsync(int take = 20, CancellationToken ct = default);

    /// <summary>Marks the quote Sent and emails the customer when SMTP is configured.</summary>
    Task SendAsync(Guid quoteId, CancellationToken ct = default);

    // Line item management (inline like Contacts on Customer)
    /// <summary>Executive may add, revise, or remove lines while the quote is pending approval.</summary>
    Task ExecutiveReviseLineAsync(QuoteLine line, CancellationToken ct = default);

    Task<Guid> ExecutiveAddLineAsync(QuoteLine line, CancellationToken ct = default);

    Task ExecutiveDeleteLineAsync(Guid lineId, CancellationToken ct = default);

    Task<Guid> AddLineAsync(QuoteLine line, CancellationToken ct = default);
    Task UpdateLineAsync(QuoteLine line, CancellationToken ct = default);
    Task DeleteLineAsync(Guid lineId, CancellationToken ct = default);

    /// <summary>
    /// Converts an accepted quote into a new Job, snapshots totals, and returns the created Job.
    /// Also updates the quote status if needed.
    /// </summary>
    Task<Job> ConvertToJobAsync(Guid quoteId, CancellationToken ct = default);
}
