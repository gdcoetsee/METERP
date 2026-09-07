namespace METERP.Common;

/// <summary>
/// Serializes EF commands on a Blazor Server circuit scope so one AppDbContext is never used concurrently.
/// Reentrant on the owner thread so nested EF commands (retry, connection setup, split queries)
/// cannot deadlock the circuit by waiting on a lock they already hold.
/// </summary>
public sealed class CircuitDbContextGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly TimeSpan _waitTimeout;
    private int _depth;
    private int _ownerThreadId;

    public CircuitDbContextGate() : this(TimeSpan.FromSeconds(15))
    {
    }

    public CircuitDbContextGate(TimeSpan waitTimeout)
    {
        _waitTimeout = waitTimeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : waitTimeout;
    }

    private bool IsReentrant =>
        _depth > 0 && Environment.CurrentManagedThreadId == _ownerThreadId;

    public async Task WaitAsync(CancellationToken ct = default)
    {
        if (IsReentrant)
        {
            _depth++;
            return;
        }

        if (!await _semaphore.WaitAsync(_waitTimeout, ct))
            throw new TimeoutException("Timed out waiting for the circuit database gate.");

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _depth = 1;
    }

    /// <summary>Synchronous wait — avoids WaitAsync().GetResult() deadlocks on the Blazor circuit sync context.</summary>
    public void Wait(CancellationToken ct = default)
    {
        if (IsReentrant)
        {
            _depth++;
            return;
        }

        if (!_semaphore.Wait(_waitTimeout, ct))
            throw new TimeoutException("Timed out waiting for the circuit database gate.");

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _depth = 1;
    }

    public void Release()
    {
        if (_depth > 1)
        {
            _depth--;
            return;
        }

        if (_depth == 1)
        {
            _depth = 0;
            _ownerThreadId = 0;
        }

        if (_semaphore.CurrentCount == 0)
            _semaphore.Release();
    }
}
