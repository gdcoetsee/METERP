using METERP.Application.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace METERP.Web.OpenTelemetry;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddMeterpOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(OpenTelemetryOptions.SectionName);
        services.Configure<OpenTelemetryOptions>(section);
        var options = section.Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        var serviceName = string.IsNullOrWhiteSpace(options.ServiceName) ? "METERP" : options.ServiceName;
        var useConsole = ShouldUseConsoleExporter(options, environment);
        var useOtlp = !string.IsNullOrWhiteSpace(options.OtlpEndpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (useConsole)
                    tracing.AddConsoleExporter();

                if (useOtlp)
                    tracing.AddOtlpExporter(otlp => ConfigureOtlpExporter(otlp, options, "traces"));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useConsole)
                    metrics.AddConsoleExporter();

                if (useOtlp)
                    metrics.AddOtlpExporter(otlp => ConfigureOtlpExporter(otlp, options, "metrics"));
            });

        return services;
    }

    private static bool ShouldUseConsoleExporter(OpenTelemetryOptions options, IHostEnvironment environment) =>
        options.EnableConsoleExporter
        || (environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.OtlpEndpoint));

    private static void ConfigureOtlpExporter(OtlpExporterOptions otlp, OpenTelemetryOptions options, string signal)
    {
        if (options.UseHttpProtobuf)
        {
            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            otlp.Endpoint = BuildHttpProtobufEndpoint(options.OtlpEndpoint!, signal);
        }
        else
        {
            otlp.Endpoint = new Uri(options.OtlpEndpoint!);
        }
    }

    private static Uri BuildHttpProtobufEndpoint(string endpoint, string signal)
    {
        var trimmed = endpoint.TrimEnd('/');
        if (trimmed.EndsWith($"/v1/{signal}", StringComparison.OrdinalIgnoreCase))
            return new Uri(trimmed);

        return new Uri($"{trimmed}/v1/{signal}");
    }
}