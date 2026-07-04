using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class SeedOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:ForceResetOnStart"] = "true",
                ["Seed:LargeDataset"] = "true",
                ["Seed:LargeDatasetCustomers"] = "100",
                ["Seed:LargeDatasetQuotes"] = "400",
                ["Seed:LargeDatasetJobs"] = "250",
                ["Seed:LargeDatasetInvoices"] = "160"
            })
            .Build();

        var options = config.GetSection(SeedOptions.SectionName).Get<SeedOptions>();

        Assert.NotNull(options);
        Assert.True(options.ForceResetOnStart);
        Assert.True(options.LargeDataset);
        Assert.Equal(100, options.LargeDatasetCustomers);
        Assert.Equal(400, options.LargeDatasetQuotes);
        Assert.Equal(250, options.LargeDatasetJobs);
        Assert.Equal(160, options.LargeDatasetInvoices);
    }

    [Fact]
    public void Defaults_AreSafeForDemoStartup()
    {
        var options = new SeedOptions();

        Assert.False(options.ForceResetOnStart);
        Assert.False(options.LargeDataset);
        Assert.Equal(50, options.LargeDatasetCustomers);
        Assert.Equal(200, options.LargeDatasetQuotes);
        Assert.Equal(120, options.LargeDatasetJobs);
        Assert.Equal(80, options.LargeDatasetInvoices);
    }
}