using METERP.Application.Options;
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
        var useConsole = options.EnableConsoleExporter
            || (environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.OtlpEndpoint));
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
                {
                    tracing.AddOtlpExporter(otlp =>
                        otlp.Endpoint = new Uri(options.OtlpEndpoint!));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (useConsole)
                    metrics.AddConsoleExporter();

                if (useOtlp)
                {
                    metrics.AddOtlpExporter(otlp =>
                        otlp.Endpoint = new Uri(options.OtlpEndpoint!));
                }
            });

        return services;
    }
}