using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Integrations;
using Xunit;

namespace METERP.Application.Tests;

public class BillingPortalServiceTests
{
    [Fact]
    public void GetCustomerPortalUrl_ReturnsNull_WhenCustomerIdMissing()
    {
        var service = new BillingPortalService(Microsoft.Extensions.Options.Options.Create(new BillingOptions
        {
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo"
        }));

        Assert.Null(service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = null }));
    }

    [Fact]
    public void GetCustomerPortalUrl_ReturnsNull_WhenPortalNotConfigured()
    {
        var service = new BillingPortalService(Microsoft.Extensions.Options.Options.Create(new BillingOptions()));
        Assert.False(service.IsConfigured);
        Assert.Null(service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = "cus_123" }));
    }

    [Fact]
    public void GetCustomerPortalUrl_AppendsCustomerQuery()
    {
        var service = new BillingPortalService(Microsoft.Extensions.Options.Options.Create(new BillingOptions
        {
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo_meterp"
        }));

        var url = service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = "cus_demo_acme" });

        Assert.NotNull(url);
        Assert.Contains("customer=cus_demo_acme", url);
        Assert.StartsWith("https://billing.stripe.com/p/login/demo_meterp", url);
    }
}