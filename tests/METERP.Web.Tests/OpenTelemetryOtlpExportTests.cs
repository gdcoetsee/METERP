using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using METERP.Web.Tests.Support;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace METERP.Web.Tests;

public class OpenTelemetryOtlpExportTests
{
    [Fact]
    public async Task HealthRequest_ExportsTracesToLoopbackOtlpCollector()
    {
        await using var collector = new LoopbackOtlpCollector();
        await using var factory = new OtlpExportWebApplicationFactory(collector.Endpoint, "METERP-Otlp-Export");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);

        await FlushTelemetryAsync(factory);
        await collector.WaitForTraceExportAsync(TimeSpan.FromSeconds(15));

        Assert.True(collector.TraceExportCount >= 1);
        Assert.Contains(collector.TracePayloads, payload =>
            Encoding.UTF8.GetString(payload).Contains("METERP-Otlp-Export", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthReadyRequest_ExportsTracesToLoopbackOtlpCollector()
    {
        await using var collector = new LoopbackOtlpCollector();
        await using var factory = new OtlpExportWebApplicationFactory(collector.Endpoint, "METERP-Otlp-Ready");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        Assert.True(response.IsSuccessStatusCode);

        await FlushTelemetryAsync(factory);
        await collector.WaitForTraceExportAsync(TimeSpan.FromSeconds(15));

        Assert.True(collector.TraceExportCount >= 1);
    }

    [Fact]
    public async Task HealthReadyRequest_ExportsMetricsToLoopbackOtlpCollector()
    {
        await using var collector = new LoopbackOtlpCollector();
        await using var factory = new OtlpExportWebApplicationFactory(collector.Endpoint, "METERP-Otlp-Ready-Metrics");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        Assert.True(response.IsSuccessStatusCode);

        await FlushTelemetryAsync(factory);
        await collector.WaitForMetricExportAsync(TimeSpan.FromSeconds(15));

        Assert.True(collector.MetricExportCount >= 1);
    }

    [Fact]
    public async Task HealthRequest_ExportsMetricsToLoopbackOtlpCollector()
    {
        await using var collector = new LoopbackOtlpCollector();
        await using var factory = new OtlpExportWebApplicationFactory(collector.Endpoint, "METERP-Otlp-Metrics");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode);

        await FlushTelemetryAsync(factory);
        await collector.WaitForMetricExportAsync(TimeSpan.FromSeconds(15));

        Assert.True(collector.MetricExportCount >= 1);
    }

    private static async Task FlushTelemetryAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tracerProvider = scope.ServiceProvider.GetService<TracerProvider>();
        tracerProvider?.ForceFlush(timeoutMilliseconds: 10_000);

        var meterProvider = scope.ServiceProvider.GetService<MeterProvider>();
        meterProvider?.ForceFlush(timeoutMilliseconds: 10_000);

        await Task.Delay(500);
    }

    private sealed class OtlpExportWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _endpoint;
        private readonly string _serviceName;
        private readonly Dictionary<string, string?> _savedEnvironment = new();

        public OtlpExportWebApplicationFactory(string endpoint, string serviceName)
        {
            _endpoint = endpoint;
            _serviceName = serviceName;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            SetEnvironmentVariable("OpenTelemetry__ServiceName", _serviceName);
            SetEnvironmentVariable("OpenTelemetry__OtlpEndpoint", _endpoint);
            SetEnvironmentVariable("OpenTelemetry__OtlpProtocol", "HttpProtobuf");
            SetEnvironmentVariable("OpenTelemetry__EnableConsoleExporter", "false");
            SetEnvironmentVariable("OTEL_BSP_SCHEDULE_DELAY", "100");
            SetEnvironmentVariable("OTEL_METRIC_EXPORT_INTERVAL", "1000");

            builder.UseSetting("OpenTelemetry:ServiceName", _serviceName);
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", _endpoint);
            builder.UseSetting("OpenTelemetry:OtlpProtocol", "HttpProtobuf");
            builder.UseSetting("OpenTelemetry:EnableConsoleExporter", "false");
        }

        private void SetEnvironmentVariable(string key, string value)
        {
            _savedEnvironment.TryAdd(key, Environment.GetEnvironmentVariable(key));
            Environment.SetEnvironmentVariable(key, value);
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var (key, value) in _savedEnvironment)
                Environment.SetEnvironmentVariable(key, value);
            base.Dispose(disposing);
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.AppDbContext>();
            db.Database.EnsureCreated();
            return host;
        }
    }
}