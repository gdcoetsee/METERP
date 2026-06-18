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