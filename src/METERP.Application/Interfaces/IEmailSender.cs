namespace METERP.Application.Interfaces;

/// <summary>
/// Sends transactional/operational email via configured SMTP (or no-ops when not configured).
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}