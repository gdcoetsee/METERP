using METERP.Application.Options;
using METERP.Application.Services;
using METERP.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class ExecutiveReportSchedulerServiceTests
{
    private static IServiceScopeFactory CreateScopeFactory(IScheduledReportService scheduledReports)
    {
        var services = new ServiceCollection();
        services.AddSingleton(scheduledReports);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotSendScheduledReports()
    {
        var scheduledReports = new Mock<IScheduledReportService>();
        var service = new ExecutiveReportSchedulerService(
            CreateScopeFactory(scheduledReports.Object),
            Microsoft.Extensions.Options.Options.Create(new ScheduledReportOptions { Enabled = false }),
            Mock.Of<ILogger<ExecutiveReportSchedulerService>>());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        scheduledReports.Verify(
            s => s.SendScheduledExecutiveReportsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_WaitsIntervalBeforeFirstSend()
    {
        var scheduledReports = new Mock<IScheduledReportService>();
        var service = new ExecutiveReportSchedulerService(
            CreateScopeFactory(scheduledReports.Object),
            Microsoft.Extensions.Options.Options.Create(new ScheduledReportOptions { Enabled = true, IntervalHours = 1 }),
            Mock.Of<ILogger<ExecutiveReportSchedulerService>>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);

        try
        {
            await Task.Delay(150, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected when stopping
        }

        await service.StopAsync(CancellationToken.None);

        scheduledReports.Verify(
            s => s.SendScheduledExecutiveReportsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_SubHourInterval_ClampsToOneHourBeforeFirstSend()
    {
        var scheduledReports = new Mock<IScheduledReportService>();
        var service = new ExecutiveReportSchedulerService(
            CreateScopeFactory(scheduledReports.Object),
            Microsoft.Extensions.Options.Options.Create(new ScheduledReportOptions { Enabled = true, IntervalHours = 0 }),
            Mock.Of<ILogger<ExecutiveReportSchedulerService>>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await service.StartAsync(cts.Token);

        try
        {
            await Task.Delay(250, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected when stopping
        }

        await service.StopAsync(CancellationToken.None);

        scheduledReports.Verify(
            s => s.SendScheduledExecutiveReportsAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}