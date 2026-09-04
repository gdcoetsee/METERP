namespace METERP.Application.Options;

/// <summary>
/// Production observability wiring. Console export is opt-in; OTLP export is used when an endpoint is set.
/// </summary>
public class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public string ServiceName { get; set; } = "METERP";

    /// <summary>OTLP gRPC/HTTP endpoint (e.g. http://otel-collector:4317). When set, traces and metrics export there.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>OTLP transport: Grpc (default, port 4317) or HttpProtobuf (port 4318).</summary>
    public string OtlpProtocol { get; set; } = "Grpc";

    /// <summary>Emit traces/metrics to console. Off by default — docker/CI logs stay readable.</summary>
    public bool EnableConsoleExporter { get; set; }

    public bool UseHttpProtobuf =>
        string.Equals(OtlpProtocol, "HttpProtobuf", StringComparison.OrdinalIgnoreCase);
}