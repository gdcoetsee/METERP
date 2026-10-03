namespace METERP.Application.Models;

/// <summary>
/// Proposal-stage deals that do not yet have a quote. Items are the bounded Home queue; TotalCount is the full waiting set.
/// </summary>
public sealed class ProposalQuoteQueueResult
{
    public int TotalCount { get; init; }

    public IReadOnlyList<ConvertibleDocumentRow> Items { get; init; } = Array.Empty<ConvertibleDocumentRow>();
}
