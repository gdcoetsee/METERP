using System.Text.RegularExpressions;
using OtpNet;

namespace METERP.E2ETests;

/// <summary>
/// Generates TOTP codes compatible with ASP.NET Core Identity authenticator tokens.
/// </summary>
internal static class TotpHelper
{
    public static string ComputeCurrentCode(string sharedKeyOrUri)
    {
        var secret = NormalizeSecret(sharedKeyOrUri);
        return new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
    }

    public static IReadOnlyList<string> GetCandidateCodes(string sharedKeyOrUri)
    {
        var secret = NormalizeSecret(sharedKeyOrUri);
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        var now = DateTime.UtcNow;
        return new[]
        {
            totp.ComputeTotp(now.AddSeconds(-30)),
            totp.ComputeTotp(now),
            totp.ComputeTotp(now.AddSeconds(30))
        }.Distinct().ToList();
    }

    private static string NormalizeSecret(string input)
    {
        if (input.Contains("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(input, @"secret=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim().ToUpperInvariant();
        }

        return input.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();
    }
}