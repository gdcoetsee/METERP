using METERP.Application.Interfaces;
using Serilog.Context;

namespace METERP.Web.Middleware;

/// <summary>
/// Pushes the current tenant id into Serilog LogContext for structured log correlation.
/// </summary>
public class TenantLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public TenantLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        var tenantId = tenantProvider.GetCurrentTenantId();
        var tenantLabel = tenantId == Guid.Empty ? "none" : tenantId.ToString();

        using (LogContext.PushProperty("TenantId", tenantLabel))
        {
            await _next(context);
        }
    }
}