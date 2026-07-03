using METERP.Application.Options;
using METERP.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Background worker that emails executive accountability summaries to tenants with NotificationEmail set.
/// </summary>
public sealed class ExecutiveReportSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduledReportOptions _options;
    private readonly ILogger<ExecutiveReportSchedulerService> _logger;

    public ExecutiveReportSchedulerService(
        IServiceScopeFactory scopeFactory,
        IOptions<ScheduledReportOptions> options,
        ILogger<ExecutiveReportSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Executive report scheduler disabled via ScheduledReports:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, _options.IntervalHours));
        _logger.LogInformation("Executive report scheduler started (interval {Hours}h).", interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Executive report scheduler run failed.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var scheduledReports = scope.ServiceProvider.GetRequiredService<IScheduledReportService>();
        var sent = await scheduledReports.SendScheduledExecutiveReportsAsync(ct);
        if (sent > 0)
            _logger.LogInformation("Scheduled executive reports sent to {Count} tenant(s).", sent);
    }
}