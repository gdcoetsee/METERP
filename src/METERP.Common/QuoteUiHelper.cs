using METERP.Domain;

namespace METERP.Common;

/// <summary>
/// UI helpers for quote entities. Breaks EF navigation cycles that break Blazor Server serialization.
/// </summary>
public static class QuoteUiHelper
{
    /// <summary>
    /// Clears QuoteLine.Quote back-references so Quote -&gt; Lines does not form a cycle in the circuit.
    /// </summary>
    public static void BreakLineCycles(Quote? quote)
    {
        if (quote?.Lines == null) return;
        foreach (var line in quote.Lines)
            line.Quote = null!;
    }

    /// <summary>
    /// Returns a shallow, navigation-safe copy for Blazor Server component state (no EF cycles).
    /// </summary>
    public static Quote? DetachForUi(Quote? source)
    {
        if (source == null) return null;

        return new Quote
        {
            Id = source.Id,
            TenantId = source.TenantId,
            CustomerId = source.CustomerId,
            QuoteNumber = source.QuoteNumber,
            QuoteDate = source.QuoteDate,
            ValidUntil = source.ValidUntil,
            Status = source.Status,
            Notes = source.Notes,
            TaxRate = source.TaxRate,
            GrossProfitPercent = source.GrossProfitPercent,
            Subtotal = source.Subtotal,
            Tax = source.Tax,
            Total = source.Total,
            Customer = source.Customer == null
                ? null
                : new Customer { Id = source.Customer.Id, Name = source.Customer.Name },
            Lines = source.Lines
                .Where(l => !l.IsDeleted)
                .Select(l => new QuoteLine
                {
                    Id = l.Id,
                    QuoteId = l.QuoteId,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitCost = l.UnitCost,
                    GrossProfitPercent = l.GrossProfitPercent,
                    UnitPrice = l.UnitPrice,
                    Unit = l.Unit,
                    LineType = l.LineType,
                    InventoryItemId = l.InventoryItemId
                })
                .ToList()
        };
    }
}