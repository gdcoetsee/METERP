using METERP.Common;
using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class QuotePrintDocumentTests
{
    private static readonly DateTime QuoteDate = new(2026, 10, 10);
    private static readonly DateTime ValidUntil = new(2026, 11, 9);

    [Fact]
    public void Build_WhenBothVatNumbersAreSet_ContainsBoth()
    {
        var text = QuotePrintDocument.Build(Sample("Mines Ltd", "4098765432"), "4012345678", "MET Electrical");

        Assert.Contains("MET Electrical — Quote", text);
        Assert.Contains("Quote: Q-1042", text);
        Assert.Contains("Our VAT number: 4012345678", text);
        Assert.Contains("Customer: Mines Ltd", text);
        Assert.Contains("Customer VAT number: 4098765432", text);
        Assert.Equal(1, Count(text, "4012345678"));
        Assert.Equal(1, Count(text, "4098765432"));
    }

    [Fact]
    public void Build_WhenCustomerVatIsBlank_StillRendersAndOmitsAFakeNumber()
    {
        var text = QuotePrintDocument.Build(Sample("Mines Ltd", "   "), "4012345678", "MET Electrical");

        Assert.Contains("Quote: Q-1042", text);
        Assert.Contains("Customer: Mines Ltd", text);
        Assert.Contains("Total: R 1150.00", text);
        Assert.Contains("Our VAT number: 4012345678", text);
        Assert.DoesNotContain("Customer VAT number", text);
        Assert.Equal(1, Count(text, "4012345678"));
        Assert.DoesNotContain("N/A", text);
        Assert.DoesNotContain("TBD", text);
        Assert.DoesNotContain("000000", text);
    }

    [Fact]
    public void Build_WhenBothVatNumbersAreBlank_StillRendersQuote()
    {
        var quote = Sample("Mines Ltd", null);
        quote.Lines = new List<QuoteLine>
        {
            new() { Description = "DB board", Quantity = 1, UnitPrice = 1000m, IsDeleted = false },
            new() { Description = "Removed", Quantity = 1, UnitPrice = 50m, IsDeleted = true }
        };

        var text = QuotePrintDocument.Build(quote, "  ", "MET Electrical", customerVatNumber: null);

        Assert.Contains("Quote: Q-1042", text);
        Assert.Contains("DB board  1 x R 1000.00  R 1000.00", text);
        Assert.DoesNotContain("Removed", text);
        Assert.DoesNotContain("Our VAT number", text);
        Assert.DoesNotContain("Customer VAT number", text);
    }

    [Fact]
    public void DetachForUi_KeepsCustomerVatNumber()
    {
        var quote = Sample("Mines Ltd", "4098765432");

        var detached = QuoteUiHelper.DetachForUi(quote);

        Assert.NotNull(detached);
        Assert.Equal("4098765432", detached!.Customer?.VatNumber);
        Assert.Equal("Mines Ltd", detached.Customer?.Name);
    }

    private static int Count(string text, string value) =>
        text.Split(value).Length - 1;

    private static Quote Sample(string customerName, string? customerVat) =>
        new()
        {
            QuoteNumber = "Q-1042",
            QuoteDate = QuoteDate,
            ValidUntil = ValidUntil,
            Subtotal = 1000m,
            Tax = 150m,
            Total = 1150m,
            Customer = new Customer { Name = customerName, VatNumber = customerVat }
        };
}
