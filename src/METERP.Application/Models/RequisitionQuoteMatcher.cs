using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// Compares a stock request with the quote on its job so an approver can see what was sold.
/// Emergency jobs may have no quote yet — that is expected, not a missing quote.
/// </summary>
public static class RequisitionQuoteMatcher
{
    public static IReadOnlyList<QuoteLine> ActiveQuoteLines(Job? job) =>
        job?.Quote?.Lines?.Where(l => !l.IsDeleted).OrderBy(l => l.Description).ToList()
        ?? [];

    public static QuoteLine? FindQuoteLine(Job? job, StockRequisitionLine request)
    {
        var lines = ActiveQuoteLines(job);
        if (request.InventoryItemId is Guid itemId && itemId != Guid.Empty)
        {
            var byItem = lines.FirstOrDefault(l => l.InventoryItemId == itemId);
            if (byItem != null)
                return byItem;
        }

        var description = request.DisplayDescription.Trim();
        if (description.Length == 0)
            return null;

        return lines.FirstOrDefault(l =>
            string.Equals(l.Description.Trim(), description, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Short label for a request line against the job's quote.</summary>
    public static string CoverageLabel(Job? job, StockRequisitionLine request)
    {
        if (job?.IsEmergency == true && job.Quote == null && job.QuoteId is null)
            return "Callout — quote comes after the work";

        var match = FindQuoteLine(job, request);
        if (match == null)
            return job?.Quote == null && job?.QuoteId is null
                ? "No quote on this job"
                : "Not on the quote";

        if (request.QuantityRequested > match.Quantity)
            return $"Quoted {match.Quantity:N2} — request is higher";

        return $"On the quote ({match.Quantity:N2})";
    }

    public static bool IsCoveredByQuote(Job? job, StockRequisitionLine request) =>
        FindQuoteLine(job, request) != null;
}
