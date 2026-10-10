using METERP.Domain;

namespace METERP.Application.Models;

/// <summary>
/// Tenant white-label settings for PDF exports and UI accents.
/// </summary>
public sealed record TenantBranding(string DisplayName, string ColorHex, string? LogoUrl, string? VatNumber = null)
{
    public const string DefaultColorHex = "#0d6efd";

    public static TenantBranding Default { get; } = new("METERP", DefaultColorHex, null);

    public static TenantBranding From(Tenant? tenant)
    {
        if (tenant == null)
            return Default;

        return new TenantBranding(
            OfficeLabel(tenant),
            NormalizeColor(tenant.BrandColorHex),
            string.IsNullOrWhiteSpace(tenant.LogoUrl) ? null : tenant.LogoUrl.Trim(),
            string.IsNullOrWhiteSpace(tenant.VatNumber) ? null : tenant.VatNumber.Trim());
    }

    /// <summary>
    /// Staff-facing name. A MET Electrical tenant never shows an Acme placeholder as its primary label.
    /// </summary>
    public static string OfficeLabel(Tenant? tenant)
    {
        if (tenant == null)
            return Default.DisplayName;

        var brand = tenant.BrandDisplayName?.Trim();
        var name = tenant.Name?.Trim() ?? "";

        if (IsMetOfficeName(name) && (string.IsNullOrWhiteSpace(brand) || IsMisleadingAcmeBrand(brand)))
            return name;

        if (!string.IsNullOrWhiteSpace(brand))
            return brand;

        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return Default.DisplayName;
    }

    public static bool IsMetOfficeTenant(Tenant? tenant) =>
        tenant != null && (IsMetOfficeName(tenant.Name) || (IsMetOfficeName(tenant.BrandDisplayName) && !IsMisleadingAcmeBrand(tenant.BrandDisplayName)));

    public static bool IsMetOfficeName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains("MET Electrical", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Acme copy that must not be the primary label on a MET Electrical tenant.
    /// </summary>
    public static bool IsMisleadingAcmeBrand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var brand = value.Trim();
        return brand.Equals("Acme Electrical", StringComparison.OrdinalIgnoreCase)
            || brand.Equals("Acme Corp", StringComparison.OrdinalIgnoreCase)
            || brand.Equals("Acme Electrical (Demo)", StringComparison.OrdinalIgnoreCase)
            || brand.Equals("Acme Corp (Demo)", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Seed rule: leave an existing MET brand alone. Write "Acme Electrical" only for a true Acme demo tenant with no brand yet.
    /// A MET tenant whose brand is still an Acme placeholder is corrected to its MET name.
    /// </summary>
    public static string? ResolveSeedBrandDisplayName(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (IsMetOfficeTenant(tenant) || IsMetOfficeName(tenant.Name))
        {
            if (IsMisleadingAcmeBrand(tenant.BrandDisplayName))
                return IsMetOfficeName(tenant.Name) ? tenant.Name.Trim() : "MET Electrical";

            return string.IsNullOrWhiteSpace(tenant.BrandDisplayName)
                ? null
                : tenant.BrandDisplayName.Trim();
        }

        if (string.IsNullOrWhiteSpace(tenant.BrandDisplayName))
            return "Acme Electrical";

        return tenant.BrandDisplayName.Trim();
    }

    private static string NormalizeColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return DefaultColorHex;

        var value = hex.Trim();
        return value.StartsWith('#') ? value : $"#{value}";
    }
}