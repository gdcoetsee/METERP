using METERP.Application.Models;
using Xunit;

namespace METERP.Application.Tests;

public class ConvertibleDocumentRowTests
{
    [Fact]
    public void CanConvertDirectly_IsTrueForQuotesAndSalesOrders()
    {
        var quote = new ConvertibleDocumentRow(Guid.NewGuid(), "Quote", "Q-1", "Acme", 1000m, "/quotes?open=1");
        var order = new ConvertibleDocumentRow(Guid.NewGuid(), "Sales order", "SO-1", "Acme", 1000m, "/sales-orders");
        var opportunity = new ConvertibleDocumentRow(Guid.NewGuid(), "Opportunity", "Plant", "Acme", 1000m, "/quotes?create=1");

        Assert.True(quote.CanConvertDirectly);
        Assert.True(order.CanConvertDirectly);
        Assert.False(opportunity.CanConvertDirectly);
    }
}
