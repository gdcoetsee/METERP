using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using METERP.Application.Interfaces;
using METERP.Web.Middleware;
using Moq;
using Serilog;
using Xunit;

namespace METERP.Web.Tests;

[Collection(nameof(TenantLoggingTestCollection))]
public class TenantLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PushesTenantIdIntoSerilogLogContext()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var capturedTenantIds = new ConcurrentBag<string>();
        using var scope = await SerilogTestLoggerScope.CreateAsync(capturedTenantIds);

        var middleware = new TenantLoggingMiddleware(_ =>
        {
            Log.Information("tenant-log-test");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

        Assert.Contains(capturedTenantIds, id => id == tenantId.ToString());
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.Empty);

        var nextCalled = false;
        var middleware = new TenantLoggingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_RestoresLogContext_AfterRequest()
    {
        var tenantId = Guid.NewGuid();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var capturedTenantIds = new ConcurrentBag<string>();
        using var scope = await SerilogTestLoggerScope.CreateAsync(capturedTenantIds);

        var middleware = new TenantLoggingMiddleware(_ =>
        {
            Log.Information("inside-request");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

        var countAfterRequest = capturedTenantIds.Count;
        Log.Information("after-request");

        Assert.Contains(capturedTenantIds, id => id == tenantId.ToString());
        Assert.Equal(countAfterRequest, capturedTenantIds.Count);
    }

    [Fact]
    public async Task InvokeAsync_UsesNone_WhenTenantIdEmpty()
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(Guid.Empty);

        var capturedTenantIds = new ConcurrentBag<string>();
        using var scope = await SerilogTestLoggerScope.CreateAsync(capturedTenantIds);

        var middleware = new TenantLoggingMiddleware(_ =>
        {
            Log.Information("tenant-log-test-empty");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), tenantProvider.Object);

        Assert.Contains(capturedTenantIds, id => id == "none");
    }
}