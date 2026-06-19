using METERP.Application.Interfaces;
using METERP.Infrastructure.Integrations;

namespace METERP.Web.Services;

/// <summary>
/// Development-only decorator that records outbound emails for E2E verification
/// even when SMTP is not configured.
/// </summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly SmtpEmailSender _inner;
    private readonly IE2EEmailCaptureStore _captureStore;

    public CapturingEmailSender(SmtpEmailSender inner, IE2EEmailCaptureStore captureStore)
    {
        _inner = inner;
        _captureStore = captureStore;
    }

    public bool IsConfigured => _inner.IsConfigured;

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (_captureStore.IsCapturing)
            _captureStore.Record(to, subject, htmlBody);

        await _inner.SendEmailAsync(to, subject, htmlBody, ct);
    }
}