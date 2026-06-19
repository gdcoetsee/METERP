using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using METERP.Application.Interfaces;
using METERP.Web.Middleware;
using Moq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace METERP.Web.Tests;

public class TenantLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PushesTenantIdIntoSerilogLogContext()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var capturedTenantIds = new ConcurrentBag<string>();
        var sink = new CollectingSink(capturedTenantIds);
        var originalLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            var middleware = new TenantLoggingMiddleware(_ =>
            {
                Log.Information("tenant-log-test");
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

            Assert.Contains(capturedTenantIds, id => id == tenantId.ToString());
        }
        finally
        {
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task InvokeAsync_UsesNone_WhenTenantIdEmpty()
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.Empty);

        var capturedTenantIds = new ConcurrentBag<string>();
        var sink = new CollectingSink(capturedTenantIds);
        var originalLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            var middleware = new TenantLoggingMiddleware(_ =>
            {
                Log.Information("tenant-log-test-empty");
                return Task.CompletedTask;
            });
            await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

            Assert.Contains(capturedTenantIds, id => id == "none");
        }
        finally
        {
            Log.Logger = originalLogger;
        }
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