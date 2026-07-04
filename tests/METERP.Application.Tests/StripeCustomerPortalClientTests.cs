using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Infrastructure.Integrations;
using Xunit;

namespace METERP.Application.Tests;

public class StripeCustomerPortalClientTests
{
    [Fact]
    public async Task CreateSessionUrlAsync_ReturnsNull_WhenCustomerIdEmpty()
    {
        var handler = new StripePortalHandler(HttpStatusCode.OK, """{"url":"https://billing.stripe.com/session/test"}""");
        var client = CreateClient(handler, new BillingOptions
        {
            StripeSecretKey = "sk_test_secret",
            CustomerPortalReturnUrl = "https://app.example/tenants"
        });

        Assert.Null(await client.CreateSessionUrlAsync("", "https://app.example/tenants"));
        Assert.Null(await client.CreateSessionUrlAsync("   ", "https://app.example/tenants"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CreateSessionUrlAsync_ReturnsNull_WhenApiNotConfigured()
    {
        var handler = new StripePortalHandler(HttpStatusCode.OK, """{"url":"https://billing.stripe.com/session/test"}""");
        var client = CreateClient(handler, new BillingOptions());

        var url = await client.CreateSessionUrlAsync("cus_123", "https://app.example/tenants");

        Assert.Null(url);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CreateSessionUrlAsync_ReturnsSessionUrl_OnSuccess()
    {
        var handler = new StripePortalHandler(
            HttpStatusCode.OK,
            """{"id":"bps_123","url":"https://billing.stripe.com/session/test_abc"}""");
        var client = CreateClient(handler, new BillingOptions
        {
            StripeSecretKey = "sk_test_secret",
            CustomerPortalReturnUrl = "https://app.example/tenants"
        });

        var url = await client.CreateSessionUrlAsync("cus_demo", "https://app.example/tenants");

        Assert.Equal("https://billing.stripe.com/session/test_abc", url);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("customer=cus_demo", handler.LastBody);
        Assert.Contains("return_url=", handler.LastBody);
        Assert.Equal("Bearer", handler.LastAuthScheme);
        Assert.Equal("sk_test_secret", handler.LastAuthParameter);
    }

    [Fact]
    public async Task CreateSessionUrlAsync_ReturnsNull_OnStripeError()
    {
        var handler = new StripePortalHandler(HttpStatusCode.BadRequest, """{"error":{"message":"No such customer"}}""");
        var client = CreateClient(handler, new BillingOptions
        {
            StripeSecretKey = "sk_test_secret",
            CustomerPortalReturnUrl = "https://app.example/tenants"
        });

        var url = await client.CreateSessionUrlAsync("cus_missing", "https://app.example/tenants");

        Assert.Null(url);
        Assert.Equal(1, handler.RequestCount);
    }

    private static StripeCustomerPortalClient CreateClient(StripePortalHandler handler, BillingOptions options)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.com/") };
        var factory = new StubHttpClientFactory(httpClient);
        return new StripeCustomerPortalClient(factory, Microsoft.Extensions.Options.Options.Create(options), NullLogger<StripeCustomerPortalClient>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StripePortalHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public string? LastAuthScheme { get; private set; }
        public string? LastAuthParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            if (request.Headers.Authorization != null)
            {
                LastAuthScheme = request.Headers.Authorization.Scheme;
                LastAuthParameter = request.Headers.Authorization.Parameter;
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}