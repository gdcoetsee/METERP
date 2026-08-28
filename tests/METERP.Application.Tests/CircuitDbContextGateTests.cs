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
}
