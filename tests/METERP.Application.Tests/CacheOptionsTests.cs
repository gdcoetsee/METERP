using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class CacheOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cache:RedisConnection"] = "localhost:6379",
                ["Cache:DefaultTtlSeconds"] = "300"
            })
            .Build();

        var options = config.GetSection(CacheOptions.SectionName).Get<CacheOptions>();

        Assert.NotNull(options);
        Assert.Equal("localhost:6379", options.RedisConnection);
        Assert.Equal(300, options.DefaultTtlSeconds);
    }

    [Fact]
    public void Defaults_UseInMemoryDistributedCache()
    {
        var options = new CacheOptions();

        Assert.Null(options.RedisConnection);
        Assert.Equal(60, options.DefaultTtlSeconds);
    }
}