using METERP.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace METERP.Application.Tests;

public class FailFastNpgsqlRetryingExecutionStrategyTests
{
    [Fact]
    public void IsPoolExhausted_DetectsNpgsqlPoolMessage()
    {
        var ex = new NpgsqlException(
            "The connection pool has been exhausted, either raise 'Max Pool Size' (currently 100) or 'Timeout' (currently 15 seconds) in your connection string.");

        Assert.True(FailFastNpgsqlRetryingExecutionStrategy.IsPoolExhausted(ex));
    }

    [Fact]
    public void IsPoolExhausted_DetectsWrappedInnerException()
    {
        var inner = new NpgsqlException("The connection pool has been exhausted, either raise 'Max Pool Size'");
        var wrapped = new InvalidOperationException("retry", inner);

        Assert.True(FailFastNpgsqlRetryingExecutionStrategy.IsPoolExhausted(wrapped));
    }

    [Fact]
    public void IsPoolExhausted_IgnoresUnrelatedFailures()
    {
        Assert.False(FailFastNpgsqlRetryingExecutionStrategy.IsPoolExhausted(new TimeoutException("gate")));
        Assert.False(FailFastNpgsqlRetryingExecutionStrategy.IsPoolExhausted(new NpgsqlException("connection refused")));
        Assert.False(FailFastNpgsqlRetryingExecutionStrategy.IsPoolExhausted(null));
    }
}
