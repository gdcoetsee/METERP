using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class OpenTelemetryOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "METERP-Test",
                ["OpenTelemetry:OtlpEndpoint"] = "http://localhost:4317",
                ["OpenTelemetry:EnableConsoleExporter"] = "true"
            })
            .Build();

        var options = config.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>();

        Assert.NotNull(options);
        Assert.Equal("METERP-Test", options.ServiceName);
        Assert.Equal("http://localhost:4317", options.OtlpEndpoint);
        Assert.True(options.EnableConsoleExporter);
    }

    [Fact]
    public void Defaults_UseMeterpServiceName()
    {
        var options = new OpenTelemetryOptions();
        Assert.Equal("METERP", options.ServiceName);
        Assert.Null(options.OtlpEndpoint);
        Assert.Equal("Grpc", options.OtlpProtocol);
        Assert.False(options.UseHttpProtobuf);
        Assert.False(options.EnableConsoleExporter);
    }

    [Fact]
    public void UseHttpProtobuf_IsTrue_WhenProtocolSet()
    {
        var options = new OpenTelemetryOptions { OtlpProtocol = "HttpProtobuf" };
        Assert.True(options.UseHttpProtobuf);
    }
}