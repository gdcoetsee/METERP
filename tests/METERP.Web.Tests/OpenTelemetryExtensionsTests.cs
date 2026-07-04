using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Web.OpenTelemetry;
using Xunit;

namespace METERP.Web.Tests;

public class OpenTelemetryExtensionsTests
{
    [Fact]
    public void AddMeterpOpenTelemetry_RegistersOptions_AndBuildsProvider()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "METERP-Unit",
                ["OpenTelemetry:EnableConsoleExporter"] = "true"
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddMeterpOpenTelemetry(config, environment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value;

        Assert.Equal("METERP-Unit", options.ServiceName);
        Assert.True(options.EnableConsoleExporter);
    }

    [Fact]
    public void AddMeterpOpenTelemetry_InDevelopmentWithoutOtlp_RegistersOptions()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        services.AddMeterpOpenTelemetry(config, environment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value;

        Assert.Equal("METERP", options.ServiceName);
        Assert.Null(options.OtlpEndpoint);
        Assert.False(options.EnableConsoleExporter);
    }

    [Fact]
    public void AddMeterpOpenTelemetry_WithHttpProtobufEndpoint_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "METERP-Http",
                ["OpenTelemetry:OtlpEndpoint"] = "http://127.0.0.1:4318",
                ["OpenTelemetry:OtlpProtocol"] = "HttpProtobuf"
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddMeterpOpenTelemetry(config, environment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value;

        Assert.True(options.UseHttpProtobuf);
        Assert.Equal("http://127.0.0.1:4318", options.OtlpEndpoint);
    }

    [Fact]
    public void AddMeterpOpenTelemetry_WithOtlpEndpoint_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenTelemetry:ServiceName"] = "METERP-Otlp",
                ["OpenTelemetry:OtlpEndpoint"] = "http://127.0.0.1:4317"
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        services.AddMeterpOpenTelemetry(config, environment);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value;

        Assert.Equal("http://127.0.0.1:4317", options.OtlpEndpoint);
    }

    [Fact]
    public async Task Host_WithOtlpConfiguration_StartsAndServesHealth()
    {
        await using var factory = new OtlpConfiguredWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Host_WithOtlpConfiguration_StartsAndServesHealthReady()
    {
        await using var factory = new OtlpConfiguredWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("database", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OtlpConfiguredWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenTelemetry:ServiceName"] = "METERP-Test",
                    ["OpenTelemetry:OtlpEndpoint"] = "http://127.0.0.1:4317",
                    ["OpenTelemetry:EnableConsoleExporter"] = "false"
                });
            });
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

    private sealed class HostingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "METERP.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}