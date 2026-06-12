using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Applies AI copilot text to real ERP documents (quote/job creation with travel-aware lines).
/// Keeps apply logic out of Blazor for testability.
/// </summary>
public interface IAiQuoteApplyService
{
    /// <summary>
    /// Creates a draft quote from AI text, adding structured suggestion lines when available
    /// or an explicit travel fallback line for contractor realism.
    /// </summary>
    Task<Quote> CreateQuoteFromAiTextAsync(
        string aiText,
        Guid customerId,
        decimal taxRate = 0.15m,
        CancellationToken ct = default);
}