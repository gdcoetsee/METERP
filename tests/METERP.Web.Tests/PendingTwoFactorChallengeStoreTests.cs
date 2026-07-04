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

    [Fact]
    public void GetChallenge_ReturnsNull_ForEmptyToken()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        Assert.Null(store.GetChallenge(""));
        Assert.Null(store.GetChallenge("   "));
    }

    [Fact]
    public void ConsumeChallenge_ReturnsNull_ForEmptyToken()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        Assert.Null(store.ConsumeChallenge(""));
        Assert.Null(store.ConsumeChallenge("   "));
    }

    [Fact]
    public async Task CreateChallenge_WithShortLifetime_Expires()
    {
        var store = new PendingTwoFactorChallengeStore(new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();

        var token = store.CreateChallenge(userId, TimeSpan.FromMilliseconds(50));
        Assert.Equal(userId, store.GetChallenge(token));

        await Task.Delay(100);

        Assert.Null(store.GetChallenge(token));
        Assert.Null(store.ConsumeChallenge(token));
    }
}