using Microsoft.Extensions.Configuration;
using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class EmailOptionsTests
{
    [Fact]
    public void Bind_FromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:SmtpHost"] = "mailpit",
                ["Email:SmtpPort"] = "1025",
                ["Email:UseSsl"] = "false",
                ["Email:FromAddress"] = "noreply@acme.demo",
                ["Email:FromName"] = "Acme ERP",
                ["Email:DefaultNotificationTo"] = "ops@acme.demo"
            })
            .Build();

        var options = config.GetSection(EmailOptions.SectionName).Get<EmailOptions>();

        Assert.NotNull(options);
        Assert.Equal("mailpit", options.SmtpHost);
        Assert.Equal(1025, options.SmtpPort);
        Assert.False(options.UseSsl);
        Assert.Equal("noreply@acme.demo", options.FromAddress);
        Assert.Equal("Acme ERP", options.FromName);
        Assert.Equal("ops@acme.demo", options.DefaultNotificationTo);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Defaults_AreDemoSafe()
    {
        var options = new EmailOptions();

        Assert.Equal(string.Empty, options.SmtpHost);
        Assert.Equal(587, options.SmtpPort);
        Assert.True(options.UseSsl);
        Assert.Equal("noreply@meterp.local", options.FromAddress);
        Assert.Equal("METERP", options.FromName);
        Assert.Null(options.DefaultNotificationTo);
        Assert.False(options.IsConfigured);
    }
}