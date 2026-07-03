namespace METERP.Application.Options;

public sealed class ScheduledReportOptions
{
    public const string SectionName = "ScheduledReports";

    /// <summary>When false, background executive report emails are disabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between scheduled report runs (default: daily).</summary>
    public int IntervalHours { get; set; } = 24;
}