using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace METERP.Web.Tests;

/// <summary>
/// Swaps <see cref="Log.Logger"/> under a process-wide lock for isolated middleware assertions.
/// </summary>
internal sealed class SerilogTestLoggerScope : IDisposable
{
    private static readonly SemaphoreSlim SwapLock = new(1, 1);

    private readonly ILogger _originalLogger;
    private bool _disposed;

    private SerilogTestLoggerScope(ILogger originalLogger) => _originalLogger = originalLogger;

    public static async Task<SerilogTestLoggerScope> CreateAsync(ConcurrentBag<string> capturedTenantIds)
    {
        await SwapLock.WaitAsync();
        var original = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CollectingSink(capturedTenantIds))
            .CreateLogger();
        return new SerilogTestLoggerScope(original);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Log.Logger = _originalLogger;
        SwapLock.Release();
        _disposed = true;
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentBag<string> _tenantIds;

        public CollectingSink(ConcurrentBag<string> tenantIds) => _tenantIds = tenantIds;

        public void Emit(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("TenantId", out var value))
                _tenantIds.Add(value.ToString().Trim('"'));
        }
    }
}