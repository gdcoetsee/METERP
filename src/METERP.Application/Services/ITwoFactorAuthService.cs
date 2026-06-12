namespace METERP.Application.Services;

/// <summary>
/// TOTP two-factor authentication via ASP.NET Identity authenticator tokens.
/// </summary>
public interface ITwoFactorAuthService
{
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Returns shared secret + otpauth URI for authenticator app setup.</summary>
    Task<TwoFactorSetupInfo?> BeginSetupAsync(Guid userId, CancellationToken ct = default);

    Task<(bool Succeeded, string[] Errors)> ConfirmSetupAsync(Guid userId, string code, CancellationToken ct = default);

    Task<(bool Succeeded, string[] Errors)> DisableAsync(Guid userId, CancellationToken ct = default);

    Task<bool> VerifyCodeAsync(Guid userId, string code, CancellationToken ct = default);
}

public record TwoFactorSetupInfo(string SharedKey, string AuthenticatorUri);