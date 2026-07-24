using METERP.Application.Production;
using Xunit;

namespace METERP.Application.Tests;

public class ProductionSecretsValidatorTests
{
    [Fact]
    public void Validate_AcceptsStrongConnectionString()
    {
        var errors = ProductionSecretsValidator.Validate(
            "Host=db;Database=METERP;Username=app;Password=S3cure!Pass;Port=5432");

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsChangeMeAndDefaultPostgres()
    {
        var errors = ProductionSecretsValidator.Validate(
            "Host=localhost;Database=METERP;Username=postgres;Password=CHANGE_ME;Port=5432");

        Assert.Contains(errors, e => e.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsDefaultPostgresPassword()
    {
        var errors = ProductionSecretsValidator.Validate(
            "Host=localhost;Database=METERP;Username=postgres;Password=postgres;Port=5432");

        Assert.Contains(errors, e => e.Contains("default postgres", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsPlaceholderBillingSecrets()
    {
        var errors = ProductionSecretsValidator.Validate(
            "Host=db;Database=METERP;Username=app;Password=S3cure!Pass;Port=5432",
            billingWebhookSecret: "whsec_...",
            stripeSecretKey: "sk_test_...");

        Assert.True(errors.Count >= 2);
        Assert.Contains(errors, e => e.Contains("WebhookSecret", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("StripeSecretKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AllowsEmptyOptionalSecrets()
    {
        var errors = ProductionSecretsValidator.Validate(
            "Host=db;Database=METERP;Username=app;Password=S3cure!Pass;Port=5432",
            billingWebhookSecret: null,
            stripeSecretKey: "",
            emailPassword: null,
            emailAuthConfigured: false);

        Assert.Empty(errors);
    }

    [Fact]
    public void EnsureValid_ThrowsWithCombinedMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionSecretsValidator.EnsureValid("Password=CHANGE_ME"));

        Assert.Contains("Production configuration is unsafe", ex.Message);
        Assert.Contains("CHANGE_ME", ex.Message);
    }

    [Theory]
    [InlineData("CHANGE_ME", true)]
    [InlineData("secret", true)]
    [InlineData("whsec_live_real_value_here", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsPlaceholderSecret_DetectsPlaceholders(string? value, bool expected)
    {
        Assert.Equal(expected, ProductionSecretsValidator.IsPlaceholderSecret(value));
    }
}
