using METERP.Common;
using Xunit;

namespace METERP.Application.Tests;

public class PaymentReceiptEmailBuilderTests
{
    [Fact]
    public void BuildHtml_PartialPayment_StatesReceivedTotalAndBalance()
    {
        // Stored VAT-inclusive total 1,150.00. This receipt is 400.00; 550.00 remains.
        var html = PaymentReceiptEmailBuilder.BuildHtml("INV-PART", 400m, 1150m, 550m);

        Assert.Contains("INV-PART", html, StringComparison.Ordinal);
        Assert.Contains("Amount received", html, StringComparison.Ordinal);
        Assert.Contains(PaymentReceiptEmailBuilder.Money(400m), html, StringComparison.Ordinal);
        Assert.Contains("Invoice total (VAT inclusive)", html, StringComparison.Ordinal);
        Assert.Contains(PaymentReceiptEmailBuilder.Money(1150m), html, StringComparison.Ordinal);
        Assert.Contains("Balance due", html, StringComparison.Ordinal);
        Assert.Contains(PaymentReceiptEmailBuilder.Money(550m), html, StringComparison.Ordinal);
        Assert.Equal("R 400.00", PaymentReceiptEmailBuilder.Money(400m));
        Assert.Equal("R 1,150.00", PaymentReceiptEmailBuilder.Money(1150m));
        Assert.Equal("R 550.00", PaymentReceiptEmailBuilder.Money(550m));
    }

    [Fact]
    public void BuildHtml_EscapesInvoiceNumber()
    {
        var html = PaymentReceiptEmailBuilder.BuildHtml("INV<1>&", 10m, 10m, 0m);

        Assert.Contains("INV&lt;1&gt;&amp;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("INV<1>", html, StringComparison.Ordinal);
    }
}
