using METERP.Domain;

namespace METERP.Application.Services;

/// <summary>
/// Optional AI assistant for intelligent quoting, estimating, and post-job learning.
/// Implementations should degrade gracefully when not configured or on error.
/// </summary>
public interface IAiAssistantService
{
    /// <summary>
    /// Whether the AI service has a valid API key and is enabled.
    /// UI should hide/disable AI buttons when false.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Given free-text scope/notes for a quote, suggest structured line items.
    /// The caller can then match suggestions to inventory and let the user approve.
    /// </summary>
    Task<AiQuoteSuggestion?> SuggestQuoteLinesAsync(
        string scopeNotes,
        decimal taxRate,
        string? customerName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deep analysis of a job's variance (quoted vs actuals including labor + all CostTypes such as Travel).
    /// Returns natural language insights + concrete recommendations.
    /// Extremely valuable for improving future bids.
    /// </summary>
    Task<AiJobAnalysis?> AnalyzeJobVarianceAsync(Job job, CancellationToken ct = default);

    /// <summary>
    /// General purpose query to the AI co-pilot with rich business context.
    /// Use for natural language questions about operations, estimates, risks, etc.
    /// </summary>
    Task<string?> AskCopilotAsync(string question, string? additionalContext = null, CancellationToken ct = default);
}

/// <summary>
/// Suggested line items from AI for a quote.
/// </summary>
public record AiQuoteSuggestion(
    string Reasoning,
    List<AiSuggestedLine> SuggestedLines
);

public record AiSuggestedLine(
    string Description,
    decimal Quantity,
    string Unit,
    string LineType,           // Material, Labour, Other
    decimal UnitPrice,
    string? SuggestedInventorySku = null,
    string? Notes = null
);

/// <summary>
/// AI post-mortem analysis of a completed or in-progress job.
/// </summary>
public record AiJobAnalysis(
    string Summary,
    string VarianceDrivers,
    List<string> Recommendations,
    string? SuggestedMarginNote = null
);