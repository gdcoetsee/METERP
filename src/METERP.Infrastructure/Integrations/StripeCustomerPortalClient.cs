using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using METERP.Application.Integrations;
using METERP.Application.Options;

namespace METERP.Infrastructure.Integrations;

public class StripeCustomerPortalClient : IStripeCustomerPortalClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BillingOptions _options;
    private readonly ILogger<StripeCustomerPortalClient> _logger;

    public StripeCustomerPortalClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BillingOptions> options,
        ILogger<StripeCustomerPortalClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> CreateSessionUrlAsync(string stripeCustomerId, string returnUrl, CancellationToken ct = default)
    {
        if (!_options.CanCreateApiSessions)
            return null;

        var customerId = stripeCustomerId.Trim();
        if (string.IsNullOrEmpty(customerId))
            return null;

        var client = _httpClientFactory.CreateClient("stripe");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/billing_portal/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.StripeSecretKey.Trim());
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = returnUrl.Trim()
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Stripe billing portal session request failed for customer {CustomerId}", customerId);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Stripe billing portal session returned {StatusCode} for customer {CustomerId}: {Body}",
                (int)response.StatusCode,
                customerId,
                body.Length > 300 ? body[..300] : body);
            return null;
        }

        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("url", out var urlElement))
            {
                var url = urlElement.GetString();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Stripe billing portal session response was not valid JSON");
        }

        return null;
    }
}