using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using METERP.Application.Options;
using METERP.Infrastructure.Integrations;
using Xunit;

namespace METERP.Application.Tests;

public class SmtpEmailSenderTests
{
    [Fact]
    public void IsConfigured_IsFalse_WhenSmtpHostEmpty()
    {
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
            NullLogger<SmtpEmailSender>.Instance);

        Assert.False(sender.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenSmtpHostSet()
    {
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "smtp.test.local" }),
            NullLogger<SmtpEmailSender>.Instance);

        Assert.True(sender.IsConfigured);
    }

    [Fact]
    public async Task SendEmailAsync_NoOps_WhenNotConfigured()
    {
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions()),
            NullLogger<SmtpEmailSender>.Instance);

        await sender.SendEmailAsync("user@test.com", "Subject", "<p>Hi</p>");
    }

    [Fact]
    public async Task SendEmailAsync_NoOps_WhenRecipientEmpty()
    {
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions { SmtpHost = "smtp.test.local" }),
            NullLogger<SmtpEmailSender>.Instance);

        await sender.SendEmailAsync("", "Subject", "<p>Hi</p>");
        await sender.SendEmailAsync("   ", "Subject", "<p>Hi</p>");
    }
}