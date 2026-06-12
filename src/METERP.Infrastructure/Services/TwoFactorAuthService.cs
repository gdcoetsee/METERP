using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using METERP.Application.Services;
using METERP.Infrastructure.Identity;

namespace METERP.Infrastructure.Services;

public class TwoFactorAuthService : ITwoFactorAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UrlEncoder _urlEncoder;

    public TwoFactorAuthService(UserManager<ApplicationUser> userManager, UrlEncoder urlEncoder)
    {
        _userManager = userManager;
        _urlEncoder = urlEncoder;
    }

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user != null && await _userManager.GetTwoFactorEnabledAsync(user);
    }

    public async Task<TwoFactorSetupInfo?> BeginSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return null;

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrWhiteSpace(key)) return null;

        var email = user.Email ?? user.UserName ?? "user";
        var uri = GenerateQrUri(email, key);
        return new TwoFactorSetupInfo(FormatKey(key), uri);
    }

    public async Task<(bool Succeeded, string[] Errors)> ConfirmSetupAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return (false, new[] { "User not found." });

        var valid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        if (!valid) return (false, new[] { "Invalid authenticator code." });

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        return (true, Array.Empty<string>());
    }

    public async Task<(bool Succeeded, string[] Errors)> DisableAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return (false, new[] { "User not found." });

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        return (true, Array.Empty<string>());
    }

    public async Task<bool> VerifyCodeAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return false;

        return await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);
    }

    private string GenerateQrUri(string email, string unformattedKey)
    {
        const string issuer = "METERP";
        return $"otpauth://totp/{_urlEncoder.Encode(issuer)}:{_urlEncoder.Encode(email)}?secret={unformattedKey}&issuer={_urlEncoder.Encode(issuer)}&digits=6";
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < unformattedKey.Length)
            result.Append(unformattedKey.AsSpan(currentPosition));

        return result.ToString().ToLowerInvariant();
    }
}