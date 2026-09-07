using METERP.Common;
using Xunit;

namespace METERP.Application.Tests;

public class CircuitDbContextGateTests
{
    [Fact]
    public async Task WaitAndRelease_AllowsAnotherWait()
    {
        var gate = new CircuitDbContextGate();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await gate.WaitAsync(cts.Token);
        gate.Release();
        await gate.WaitAsync(cts.Token);
        gate.Release();
    }

    [Fact]
    public void ExtraRelease_DoesNotThrow()
    {
        var gate = new CircuitDbContextGate();
        gate.Release();
        gate.Release();
    }

    [Fact]
    public void Wait_ThenRelease_AllowsAnotherWait()
    {
        var gate = new CircuitDbContextGate();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        gate.Wait(cts.Token);
        gate.Release();
        gate.Wait(cts.Token);
        gate.Release();
    }

    [Fact]
    public async Task NestedWaitAsync_DoesNotDeadlock()
    {
        var gate = new CircuitDbContextGate(TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await gate.WaitAsync(cts.Token);
        // Nested acquire must work on the same thread (Blazor circuit / sync EF interceptors).
        gate.Wait(cts.Token);
        gate.Release();
        gate.Release();
        await gate.WaitAsync(cts.Token);
        gate.Release();
    }

    [Fact]
    public void NestedWait_DoesNotDeadlock()
    {
        var gate = new CircuitDbContextGate(TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        gate.Wait(cts.Token);
        gate.Wait(cts.Token);
        gate.Release();
        gate.Release();
        gate.Wait(cts.Token);
        gate.Release();
    }

    [Fact]
    public async Task Wait_TimesOut_WhenAnotherHolderNeverReleases()
    {
        var gate = new CircuitDbContextGate(TimeSpan.FromMilliseconds(80));
        using var entered = new SemaphoreSlim(0, 1);
        using var release = new SemaphoreSlim(0, 1);

        var holder = Task.Run(async () =>
        {
            gate.Wait();
            entered.Release();
            await release.WaitAsync();
            gate.Release();
        });

        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(2)));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => gate.Wait());
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), "Gate wait timeout should fail fast.");

        release.Release();
        await holder.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
