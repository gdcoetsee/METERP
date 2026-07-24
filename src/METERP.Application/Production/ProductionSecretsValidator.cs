namespace METERP.Application.Production;

/// <summary>
/// Validates production configuration so the app fails fast on placeholder secrets.
/// Pure logic — unit tested without hosting.
/// </summary>
public static class ProductionSecretsValidator
{
    public static IReadOnlyList<string> Validate(
        string? connectionString,
        string? billingWebhookSecret = null,
        string? stripeSecretKey = null,
        string? emailPassword = null,
        bool emailAuthConfigured = false)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings:DefaultConnection is required in Production.");
        }
        else
        {
            if (connectionString.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
                errors.Add("ConnectionStrings:DefaultConnection must not use CHANGE_ME placeholders.");

            if (connectionString.Contains("Password=postgres", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("Password=postgres;", StringComparison.OrdinalIgnoreCase))
                errors.Add("ConnectionStrings:DefaultConnection must not use the default postgres password.");
        }

        if (IsPlaceholderSecret(billingWebhookSecret))
            errors.Add("Billing:WebhookSecret looks like a placeholder — set a real Stripe webhook secret or clear it.");

        if (IsPlaceholderSecret(stripeSecretKey))
            errors.Add("Billing:StripeSecretKey looks like a placeholder — set a real key or clear it.");

        if (emailAuthConfigured && IsPlaceholderSecret(emailPassword))
            errors.Add("Email:Password looks like a placeholder while SMTP auth is enabled.");

        return errors;
    }

    public static void EnsureValid(
        string? connectionString,
        string? billingWebhookSecret = null,
        string? stripeSecretKey = null,
        string? emailPassword = null,
        bool emailAuthConfigured = false)
    {
        var errors = Validate(connectionString, billingWebhookSecret, stripeSecretKey, emailPassword, emailAuthConfigured);
        if (errors.Count == 0) return;

        throw new InvalidOperationException(
            "Production configuration is unsafe:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => " - " + e)));
    }

    public static bool IsPlaceholderSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false; // empty = feature disabled, not a bad secret

        var v = value.Trim();
        if (v.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            return true;
        if (v.Equals("secret", StringComparison.OrdinalIgnoreCase)
            || v.Equals("password", StringComparison.OrdinalIgnoreCase)
            || v.Equals("changeme", StringComparison.OrdinalIgnoreCase))
            return true;
        if (v is "whsec_..." or "sk_test_..." or "sk_live_...")
            return true;
        if (v.StartsWith("whsec_your", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("sk_your", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
