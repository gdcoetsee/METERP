using Microsoft.Extensions.Logging.Abstractions;
using METERP.Application.Options;
using METERP.Application.Tests.Support;
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

    [Fact]
    public async Task SendEmailAsync_DeliversToLoopbackSmtpServer()
    {
        await using var server = new LoopbackSmtpServer();
        var sender = new SmtpEmailSender(
            Microsoft.Extensions.Options.Options.Create(new EmailOptions
            {
                SmtpHost = "127.0.0.1",
                SmtpPort = server.Port,
                UseSsl = false,
                FromAddress = "noreply@test.local",
                FromName = "METERP Test"
            }),
            NullLogger<SmtpEmailSender>.Instance);

        await sender.SendEmailAsync("billing@acme.demo", "Invoice INV-001 created", "<p>Total: R 1,500</p>");
        await server.WaitForMessageAsync(TimeSpan.FromSeconds(10));

        var message = Assert.Single(server.ReceivedMessages);
        Assert.Contains("Invoice INV-001 created", message);
        Assert.Contains("billing@acme.demo", message);
        Assert.Contains("Total: R 1,500", message);
    }
}