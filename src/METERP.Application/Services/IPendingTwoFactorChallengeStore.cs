namespace METERP.Application.Services;

/// <summary>
/// Short-lived store between password verification and TOTP verification during login.
/// </summary>
public interface IPendingTwoFactorChallengeStore
{
    string CreateChallenge(Guid userId, TimeSpan? lifetime = null);

    Guid? GetChallenge(string token);

    Guid? ConsumeChallenge(string token);
}