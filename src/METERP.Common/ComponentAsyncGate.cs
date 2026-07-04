namespace METERP.Common;

/// <summary>
/// Serializes async data loads in Blazor Server components to avoid DbContext concurrency on a circuit scope.
/// </summary>
public sealed class ComponentAsyncGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}