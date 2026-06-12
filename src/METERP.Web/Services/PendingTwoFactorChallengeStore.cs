using Microsoft.Extensions.Caching.Memory;
using METERP.Application.Services;

namespace METERP.Web.Services;

public class PendingTwoFactorChallengeStore : IPendingTwoFactorChallengeStore
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    public PendingTwoFactorChallengeStore(IMemoryCache cache) => _cache = cache;

    public string CreateChallenge(Guid userId, TimeSpan? lifetime = null)
    {
        var token = Guid.NewGuid().ToString("N");
        _cache.Set(GetKey(token), userId, lifetime ?? DefaultLifetime);
        return token;
    }

    public Guid? GetChallenge(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        return _cache.TryGetValue(GetKey(token), out Guid userId) ? userId : null;
    }

    public Guid? ConsumeChallenge(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var key = GetKey(token);
        if (!_cache.TryGetValue(key, out Guid userId)) return null;
        _cache.Remove(key);
        return userId;
    }

    private static string GetKey(string token) => $"2fa-challenge:{token}";
}