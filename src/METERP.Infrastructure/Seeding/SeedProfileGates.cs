using Microsoft.Extensions.Configuration;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Decides whether startup should load the MET Electrical FY profile or the Acme/E2E demo fixtures.
/// Acme/E2E stays the default so CI keeps working when the flag is unset.
/// </summary>
public static class SeedProfileGates
{
    /// <summary>
    /// MET FY demo when the profile is MET, or when E2E seeding is explicitly turned off.
    /// </summary>
    public static bool IsMet(string? profile, string? seedE2E)
    {
        if (string.Equals(profile, "MET", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(seedE2E, "false", StringComparison.OrdinalIgnoreCase)
            || seedE2E == "0";
    }

    public static bool IsMetSeedProfile(IConfiguration config)
    {
        var profile = Environment.GetEnvironmentVariable("METERP_SEED_PROFILE");
        if (string.IsNullOrWhiteSpace(profile))
            profile = config["Seed:Profile"];

        return IsMet(profile, Environment.GetEnvironmentVariable("METERP_SEED_E2E"));
    }

    public static AccessImportMode ParseAccessImportFlag(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return AccessImportMode.Off;

        if (string.Equals(flag, "dry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "dry-run", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "dryrun", StringComparison.OrdinalIgnoreCase))
            return AccessImportMode.DryRun;

        if (string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "write", StringComparison.OrdinalIgnoreCase))
            return AccessImportMode.Write;

        return AccessImportMode.Off;
    }
}

public enum AccessImportMode
{
    Off = 0,
    DryRun = 1,
    Write = 2
}
