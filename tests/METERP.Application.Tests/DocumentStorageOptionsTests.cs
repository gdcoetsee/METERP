using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class DocumentStorageOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentStorage:RootPath"] = "tenant-uploads"
            })
            .Build();

        var options = config.GetSection(DocumentStorageOptions.SectionName).Get<DocumentStorageOptions>();

        Assert.NotNull(options);
        Assert.Equal("tenant-uploads", options.RootPath);
    }

    [Fact]
    public void Defaults_UseUploadsFolder()
    {
        var options = new DocumentStorageOptions();
        Assert.Equal("uploads", options.RootPath);
    }
}