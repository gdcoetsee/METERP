using Microsoft.Extensions.Caching.Memory;
using METERP.Web.Services;
using Xunit;

namespace METERP.Web.Tests;

public class PendingTwoFactorChallengeStoreTests
{
    [Fact]
    public void CreateChallenge_ReturnsToken_AndGetChallengeResolvesUser()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();

        var token = store.CreateChallenge(userId);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(userId, store.GetChallenge(token));
    }

    [Fact]
    public void ConsumeChallenge_RemovesToken_AndReturnsUserOnce()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();
        var token = store.CreateChallenge(userId);

        Assert.Equal(userId, store.ConsumeChallenge(token));
        Assert.Null(store.GetChallenge(token));
        Assert.Null(store.ConsumeChallenge(token));
    }

    [Fact]
    public void GetChallenge_ReturnsNull_ForUnknownToken()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        Assert.Null(store.GetChallenge("not-a-real-token"));
    }
}