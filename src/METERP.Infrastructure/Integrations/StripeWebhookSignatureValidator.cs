using System.Security.Cryptography;
using System.Text;

namespace METERP.Infrastructure.Integrations;

/// <summary>
/// Validates Stripe-Signature header (t=timestamp,v1=hmac).
/// </summary>
internal static class StripeWebhookSignatureValidator
{
    private const int DefaultToleranceSeconds = 300;

    public static bool IsValid(string payload, string? signatureHeader, string webhookSecret, int toleranceSeconds = DefaultToleranceSeconds)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(webhookSecret))
            return false;

        long? timestamp = null;
        string? v1 = null;

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key == "t" && long.TryParse(value, out var ts))
                timestamp = ts;
            else if (key == "v1")
                v1 = value;
        }

        if (timestamp == null || string.IsNullOrEmpty(v1))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp.Value) > toleranceSeconds)
            return false;

        var signedPayload = $"{timestamp.Value}.{payload}";
        var expected = ComputeHmacHex(webhookSecret, signedPayload);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(v1));
    }

    public static string BuildSignatureHeader(string webhookSecret, string payload, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{ts}.{payload}";
        var sig = ComputeHmacHex(webhookSecret, signedPayload);
        return $"t={ts},v1={sig}";
    }

    private static string ComputeHmacHex(string secret, string signedPayload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}