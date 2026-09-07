using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace METERP.Infrastructure.Persistence;

/// <summary>
/// Standard Npgsql retries except pool exhaustion — retrying an empty pool makes E2E/circuit hangs worse.
/// </summary>
public sealed class FailFastNpgsqlRetryingExecutionStrategy : NpgsqlRetryingExecutionStrategy
{
    public FailFastNpgsqlRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null)
    {
    }

    protected override bool ShouldRetryOn(Exception? exception) =>
        !IsPoolExhausted(exception) && base.ShouldRetryOn(exception);

    public static bool IsPoolExhausted(Exception? exception)
    {
        for (var ex = exception; ex != null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException npg
                && npg.Message.Contains("connection pool has been exhausted", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
