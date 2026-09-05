namespace METERP.Common;

/// <summary>
/// Serializes EF commands on a Blazor Server circuit scope so one AppDbContext is never used concurrently.
/// </summary>
public sealed class CircuitDbContextGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _semaphore.WaitAsync(ct);

    /// <summary>Synchronous wait — avoids WaitAsync().GetResult() deadlocks on the Blazor circuit sync context.</summary>
    public void Wait(CancellationToken ct = default) => _semaphore.Wait(ct);

    public void Release()
    {
        if (_semaphore.CurrentCount == 0)
            _semaphore.Release();
    }
}