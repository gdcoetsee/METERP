namespace METERP.Domain;

/// <summary>
/// Pure pricing helpers for quote lines (gross profit on revenue).
/// GP% = (Sell - Cost) / Sell  =>  Sell = Cost / (1 - GP%)
/// </summary>
public static class QuotePricing
{
    public static decimal SellPriceFromCost(decimal unitCost, decimal grossProfitPercent)
    {
        if (unitCost <= 0) return 0;
        if (grossProfitPercent <= 0) return unitCost;
        if (grossProfitPercent >= 1) return unitCost * 2;
        return Math.Round(unitCost / (1 - grossProfitPercent), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Unit price stored on the quote line. Ex-VAT entry is kept as typed.
    /// A VAT-inclusive entry is split once so <see cref="Quote.RecalculateTotals"/>
    /// can add VAT on the ex-VAT subtotal. Travel uses the same switch as every other line.
    /// Gross R 115.00 at 15% stores 100.00; the quote then carries tax 15.00 and total 115.00.
    /// </summary>
    public static decimal UnitPriceFromEntry(decimal enteredUnitPrice, decimal taxRate, bool priceIncludesVat)
    {
        if (!priceIncludesVat)
            return enteredUnitPrice;

        return InvoiceBillingCalculator.SplitVatInclusive(enteredUnitPrice, taxRate).Subtotal;
    }

    public static decimal LineGrossProfit(QuoteLine line) =>
        line.LineTotal - (line.UnitCost * line.Quantity);

    public static decimal QuoteGrossProfit(IEnumerable<QuoteLine> lines) =>
        lines.Where(l => !l.IsDeleted).Sum(LineGrossProfit);

    /// <summary>
    /// Weighted GP% on revenue across all lines: total GP ÷ subtotal.
    /// </summary>
    public static decimal BlendedGrossProfitPercent(decimal subtotal, decimal totalGrossProfit) =>
        subtotal > 0 ? Math.Round(totalGrossProfit / subtotal * 100, 1, MidpointRounding.AwayFromZero) : 0;
}