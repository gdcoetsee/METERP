using METERP.Infrastructure.Integrations;
using Xunit;

namespace METERP.Application.Tests;

public class StripeWebhookSignatureValidatorTests
{
    private const string Secret = "whsec_test_signature_validator";

    [Fact]
    public void IsValid_ReturnsTrue_WhenSignatureMatches()
    {
        const string payload = """{ "id": "evt_sig_ok", "type": "ping" }""";
        var header = StripeWebhookSignatureValidator.BuildSignatureHeader(Secret, payload);

        Assert.True(StripeWebhookSignatureValidator.IsValid(payload, header, Secret));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenSignatureTampered()
    {
        const string payload = """{ "id": "evt_sig_bad", "type": "ping" }""";
        var header = StripeWebhookSignatureValidator.BuildSignatureHeader(Secret, payload);

        Assert.False(StripeWebhookSignatureValidator.IsValid(payload, header, "whsec_other_secret"));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenTimestampExpired()
    {
        const string payload = """{ "id": "evt_sig_old", "type": "ping" }""";
        var expiredTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var header = StripeWebhookSignatureValidator.BuildSignatureHeader(Secret, payload, expiredTimestamp);

        Assert.False(StripeWebhookSignatureValidator.IsValid(payload, header, Secret, toleranceSeconds: 60));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenHeaderMissing()
    {
        Assert.False(StripeWebhookSignatureValidator.IsValid("{}", null, Secret));
        Assert.False(StripeWebhookSignatureValidator.IsValid("{}", "", Secret));
    }

    [Fact]
    public void IsValid_ReturnsFalse_WhenHeaderMalformed()
    {
        Assert.False(StripeWebhookSignatureValidator.IsValid("{}", "not-a-stripe-header", Secret));
    }
}