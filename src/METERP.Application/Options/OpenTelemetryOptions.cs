namespace METERP.Application.Options;

/// <summary>
/// Production observability wiring. Console export is used in Development when OTLP is not set.
/// </summary>
public class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public string ServiceName { get; set; } = "METERP";

    /// <summary>OTLP gRPC/HTTP endpoint (e.g. http://otel-collector:4317). When set, traces and metrics export there.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>Emit traces/metrics to console (default true in Development when OTLP is empty).</summary>
    public bool EnableConsoleExporter { get; set; }
}