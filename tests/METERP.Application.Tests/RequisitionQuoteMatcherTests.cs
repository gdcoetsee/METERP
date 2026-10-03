using METERP.Application.Models;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class RequisitionQuoteMatcherTests
{
    [Fact]
    public void Emergency_Without_Quote_Is_Not_Treated_As_A_Missing_Sale()
    {
        var job = new Job { IsEmergency = true, Title = "After-hours fault" };
        var line = new StockRequisitionLine { Description = "Cable joint", QuantityRequested = 2 };

        Assert.Null(RequisitionQuoteMatcher.FindQuoteLine(job, line));
        Assert.Equal("Callout — quote comes after the work", RequisitionQuoteMatcher.CoverageLabel(job, line));
    }

    [Fact]
    public void Matches_Request_To_Quote_Line_By_Stock_Item()
    {
        var itemId = Guid.NewGuid();
        var job = QuotedJob(
            new QuoteLine { InventoryItemId = itemId, Description = "Different wording", Quantity = 4, UnitPrice = 10, LineType = "Material" },
            new QuoteLine { Description = "Deleted", Quantity = 9, UnitPrice = 1, IsDeleted = true });
        var line = new StockRequisitionLine { InventoryItemId = itemId, Description = "Cable", QuantityRequested = 4 };

        var match = RequisitionQuoteMatcher.FindQuoteLine(job, line);

        Assert.Equal(itemId, match!.InventoryItemId);
        Assert.Equal($"On the quote ({4m.ToString("N2")})", RequisitionQuoteMatcher.CoverageLabel(job, line));
    }

    [Fact]
    public void Matches_Free_Text_Request_By_Description_And_Flags_A_Higher_Quantity()
    {
        var job = QuotedJob(new QuoteLine { Description = "  16mm gland ", Quantity = 2, UnitPrice = 15, LineType = "Material" });
        var line = new StockRequisitionLine { Description = "16mm gland", QuantityRequested = 5 };

        Assert.Equal($"Quoted {2m.ToString("N2")} — request is higher", RequisitionQuoteMatcher.CoverageLabel(job, line));
    }

    [Fact]
    public void Quoted_Job_Flags_Stock_That_Was_Not_Sold()
    {
        var job = QuotedJob(new QuoteLine { Description = "Labour", Quantity = 8, UnitPrice = 195, LineType = "Labour" });
        var line = new StockRequisitionLine { Description = "Unquoted breaker", QuantityRequested = 1 };

        Assert.False(RequisitionQuoteMatcher.IsCoveredByQuote(job, line));
        Assert.Equal("Not on the quote", RequisitionQuoteMatcher.CoverageLabel(job, line));
    }

    [Fact]
    public void Job_With_No_Quote_Says_So()
    {
        var job = new Job { IsEmergency = false };
        var line = new StockRequisitionLine { Description = "Tape", QuantityRequested = 1 };

        Assert.Equal("No quote on this job", RequisitionQuoteMatcher.CoverageLabel(job, line));
    }

    private static Job QuotedJob(params QuoteLine[] lines) => new()
    {
        QuoteId = Guid.NewGuid(),
        Quote = new Quote
        {
            QuoteNumber = "Q-1",
            Lines = lines.ToList()
        }
    };
}
