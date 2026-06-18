using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class QuotePricingTests
{
    [Theory]
    [InlineData(100, 0.25, 133.33)]
    [InlineData(100, 0, 100)]
    [InlineData(0, 0.25, 0)]
    [InlineData(50, 0.5, 100)]
    public void SellPriceFromCost_AppliesGrossProfitOnRevenue(decimal cost, decimal gp, decimal expected)
    {
        var sell = QuotePricing.SellPriceFromCost(cost, gp);
        Assert.Equal(expected, sell);
    }

    [Fact]
    public void LineGrossProfit_SubtractsCostFromLineTotal()
    {
        var line = new QuoteLine { Quantity = 2, UnitCost = 40, UnitPrice = 100 };
        Assert.Equal(120m, QuotePricing.LineGrossProfit(line));
    }

    [Fact]
    public void QuoteGrossProfit_SumsNonDeletedLines()
    {
        var lines = new List<QuoteLine>
        {
            new() { Quantity = 1, UnitCost = 50, UnitPrice = 100, IsDeleted = false },
            new() { Quantity = 1, UnitCost = 20, UnitPrice = 40, IsDeleted = true }
        };

        Assert.Equal(50m, QuotePricing.QuoteGrossProfit(lines));
    }

    [Fact]
    public void BlendedGrossProfitPercent_WeightsBySellTotal()
    {
        // Line A: sell 133.33, GP 33.33 (25%). Line B: sell 333.33, GP 133.33 (40%).
        var subtotal = 133.33m + 333.33m;
        var totalGp = 33.33m + 133.33m;
        var blended = QuotePricing.BlendedGrossProfitPercent(subtotal, totalGp);
        Assert.Equal(35.7m, blended);
    }

    [Fact]
    public void BlendedGrossProfitPercent_ReturnsZero_WhenSubtotalEmpty()
    {
        Assert.Equal(0m, QuotePricing.BlendedGrossProfitPercent(0, 100));
    }
}