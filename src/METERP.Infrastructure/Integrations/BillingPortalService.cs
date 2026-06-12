using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Integrations;

public class BillingPortalService : IBillingPortalService
{
    private readonly BillingOptions _options;

    public BillingPortalService(IOptions<BillingOptions> options) => _options = options.Value;

    public bool IsConfigured => _options.IsPortalConfigured;

    public string? GetCustomerPortalUrl(Tenant tenant)
    {
        if (!_options.IsPortalConfigured || string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
            return null;

        var customerId = tenant.StripeCustomerId.Trim();
        if (string.IsNullOrEmpty(customerId))
            return null;

        var baseUrl = _options.CustomerPortalBaseUrl.Trim().TrimEnd('/');
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{baseUrl}{separator}customer={Uri.EscapeDataString(customerId)}";
    }
}