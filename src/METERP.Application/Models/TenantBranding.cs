using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// Tenant white-label settings for PDF exports and UI accents.
/// </summary>
public sealed record TenantBranding(string DisplayName, string ColorHex, string? LogoUrl)
{
    public const string DefaultColorHex = "#0d6efd";

    public static TenantBranding Default { get; } = new("METERP", DefaultColorHex, null);

    public static TenantBranding From(Tenant? tenant)
    {
        if (tenant == null)
            return Default;

        return new TenantBranding(
            string.IsNullOrWhiteSpace(tenant.BrandDisplayName) ? tenant.Name : tenant.BrandDisplayName.Trim(),
            NormalizeColor(tenant.BrandColorHex),
            string.IsNullOrWhiteSpace(tenant.LogoUrl) ? null : tenant.LogoUrl.Trim());
    }

    private static string NormalizeColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return DefaultColorHex;

        var value = hex.Trim();
        return value.StartsWith('#') ? value : $"#{value}";
    }
}