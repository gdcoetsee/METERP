using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class BillingOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:WebhookSecret"] = "whsec_test",
                ["Billing:StripeSecretKey"] = "sk_test_123",
                ["Billing:CustomerPortalBaseUrl"] = "https://billing.stripe.com/p/login/test",
                ["Billing:CustomerPortalReturnUrl"] = "https://acme.demo/account",
                ["Billing:WebhookEventRetentionDays"] = "30"
            })
            .Build();

        var options = config.GetSection(BillingOptions.SectionName).Get<BillingOptions>();

        Assert.NotNull(options);
        Assert.Equal("whsec_test", options.WebhookSecret);
        Assert.Equal("sk_test_123", options.StripeSecretKey);
        Assert.Equal("https://billing.stripe.com/p/login/test", options.CustomerPortalBaseUrl);
        Assert.Equal("https://acme.demo/account", options.CustomerPortalReturnUrl);
        Assert.Equal(30, options.WebhookEventRetentionDays);
    }

    [Fact]
    public void Default_WebhookEventRetentionDays_IsNinety()
    {
        var options = new BillingOptions();
        Assert.Equal(90, options.WebhookEventRetentionDays);
    }

    [Fact]
    public void ComputedFlags_ReflectConfiguredSecrets()
    {
        var empty = new BillingOptions();
        Assert.False(empty.IsSignatureRequired);
        Assert.False(empty.IsApiConfigured);
        Assert.False(empty.CanCreateApiSessions);
        Assert.False(empty.IsPortalConfigured);

        var portalOnly = new BillingOptions { CustomerPortalBaseUrl = "https://billing.stripe.com/p/login/demo" };
        Assert.True(portalOnly.IsPortalConfigured);
        Assert.False(portalOnly.CanCreateApiSessions);

        var apiReady = new BillingOptions
        {
            StripeSecretKey = "sk_test_abc",
            CustomerPortalReturnUrl = "https://acme.demo/account"
        };
        Assert.True(apiReady.IsApiConfigured);
        Assert.True(apiReady.CanCreateApiSessions);
        Assert.True(apiReady.IsPortalConfigured);

        var signed = new BillingOptions { WebhookSecret = "whsec_live" };
        Assert.True(signed.IsSignatureRequired);
    }
}