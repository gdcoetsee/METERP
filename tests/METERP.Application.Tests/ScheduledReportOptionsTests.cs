using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class ScheduledReportOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ScheduledReports:Enabled"] = "false",
                ["ScheduledReports:IntervalHours"] = "12"
            })
            .Build();

        var options = config.GetSection(ScheduledReportOptions.SectionName).Get<ScheduledReportOptions>();

        Assert.NotNull(options);
        Assert.False(options.Enabled);
        Assert.Equal(12, options.IntervalHours);
    }

    [Fact]
    public void Defaults_EnableDailyScheduler()
    {
        var options = new ScheduledReportOptions();

        Assert.True(options.Enabled);
        Assert.Equal(24, options.IntervalHours);
    }
}