namespace METERP.Application.Options;

/// <summary>
/// SMTP settings for operational notifications (invoice alerts, low stock, etc.).
/// When SmtpHost is empty, email sending is skipped (demo-safe default).
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@meterp.local";
    public string FromName { get; set; } = "METERP";

    /// <summary>Fallback recipient when tenant.NotificationEmail is not set.</summary>
    public string? DefaultNotificationTo { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
}