namespace METERP.Common;

/// <summary>
/// Serializes EF commands on a Blazor Server circuit scope so one AppDbContext is never used concurrently.
/// </summary>
public sealed class CircuitDbContextGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task WaitAsync(CancellationToken ct = default) => _semaphore.WaitAsync(ct);

    public void Release()
    {
        if (_semaphore.CurrentCount == 0)
            _semaphore.Release();
    }
}