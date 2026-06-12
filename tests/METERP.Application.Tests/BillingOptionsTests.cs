using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class BillingOptionsTests
{
    [Fact]
    public void Default_WebhookEventRetentionDays_IsNinety()
    {
        var options = new BillingOptions();
        Assert.Equal(90, options.WebhookEventRetentionDays);
    }
}