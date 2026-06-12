using Microsoft.Extensions.Options;
using METERP.Application.Integrations;
using METERP.Application.Options;
using METERP.Domain;
using METERP.Infrastructure.Integrations;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class BillingPortalServiceTests
{
    private static BillingPortalService CreateService(
        BillingOptions options,
        IStripeCustomerPortalClient? portalClient = null)
    {
        var client = portalClient ?? new Mock<IStripeCustomerPortalClient>().Object;
        return new BillingPortalService(Microsoft.Extensions.Options.Options.Create(options), client);
    }

    [Fact]
    public void GetCustomerPortalUrl_ReturnsNull_WhenCustomerIdMissing()
    {
        var service = CreateService(new BillingOptions
        {
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo"
        });

        Assert.Null(service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = null }));
    }

    [Fact]
    public void GetCustomerPortalUrl_ReturnsNull_WhenPortalNotConfigured()
    {
        var service = CreateService(new BillingOptions());
        Assert.False(service.IsConfigured);
        Assert.Null(service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = "cus_123" }));
    }

    [Fact]
    public void GetCustomerPortalUrl_AppendsCustomerQuery()
    {
        var service = CreateService(new BillingOptions
        {
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo_meterp"
        });

        var url = service.GetCustomerPortalUrl(new Tenant { StripeCustomerId = "cus_demo_acme" });

        Assert.NotNull(url);
        Assert.Contains("customer=cus_demo_acme", url);
        Assert.StartsWith("https://billing.stripe.com/p/login/demo_meterp", url);
    }

    [Fact]
    public async Task ResolveCustomerPortalUrlAsync_UsesApiSession_WhenConfigured()
    {
        var portalClient = new Mock<IStripeCustomerPortalClient>();
        portalClient
            .Setup(c => c.CreateSessionUrlAsync("cus_live", "https://app.example/tenants", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://billing.stripe.com/session/live_test");

        var service = CreateService(new BillingOptions
        {
            StripeSecretKey = "sk_test_key",
            CustomerPortalReturnUrl = "https://app.example/tenants",
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo"
        }, portalClient.Object);

        var url = await service.ResolveCustomerPortalUrlAsync(new Tenant { StripeCustomerId = "cus_live" });

        Assert.Equal("https://billing.stripe.com/session/live_test", url);
        portalClient.Verify(c => c.CreateSessionUrlAsync("cus_live", "https://app.example/tenants", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveCustomerPortalUrlAsync_FallsBackToStatic_WhenApiReturnsNull()
    {
        var portalClient = new Mock<IStripeCustomerPortalClient>();
        portalClient
            .Setup(c => c.CreateSessionUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = CreateService(new BillingOptions
        {
            StripeSecretKey = "sk_test_key",
            CustomerPortalReturnUrl = "https://app.example/tenants",
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo_meterp"
        }, portalClient.Object);

        var url = await service.ResolveCustomerPortalUrlAsync(new Tenant { StripeCustomerId = "cus_demo_acme" });

        Assert.NotNull(url);
        Assert.Contains("customer=cus_demo_acme", url);
    }

    [Fact]
    public async Task ResolveCustomerPortalUrlAsync_UsesStatic_WhenApiNotConfigured()
    {
        var portalClient = new Mock<IStripeCustomerPortalClient>();
        var service = CreateService(new BillingOptions
        {
            CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo_meterp"
        }, portalClient.Object);

        var url = await service.ResolveCustomerPortalUrlAsync(new Tenant { StripeCustomerId = "cus_demo_acme" });

        Assert.NotNull(url);
        Assert.Contains("customer=cus_demo_acme", url);
        portalClient.Verify(
            c => c.CreateSessionUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}