using Microsoft.Extensions.Configuration;

namespace METERP.Infrastructure.Seeding;

/// <summary>
/// Startup gates. Demo/E2E fixtures and destructive reset stay off unless explicitly enabled.
/// The MET profile only chooses which demo shape to load after <see cref="IsDemoSeedEnabled(IConfiguration)"/> is true.
/// </summary>
public static class SeedProfileGates
{
    /// <summary>
    /// MET FY demo when the profile is MET, or when E2E seeding is explicitly turned off.
    /// This does not enable seeding by itself.
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

    /// <summary>True for <c>true</c>, <c>1</c>, or <c>yes</c>. Anything else, including empty, is false.</summary>
    public static bool IsExplicitlyEnabled(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return false;

        return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || flag == "1"
            || string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A non-empty environment flag wins. When it is absent, <paramref name="configEnabled"/> is used.
    /// </summary>
    public static bool IsEnabled(string? environmentFlag, bool configEnabled)
    {
        if (!string.IsNullOrWhiteSpace(environmentFlag))
            return IsExplicitlyEnabled(environmentFlag);

        return configEnabled;
    }

    /// <summary><c>METERP_SEED_DEMO=true</c> or config <c>Seed:Demo=true</c>. Default false.</summary>
    public static bool IsDemoSeedEnabled(string? environmentFlag, bool configEnabled) =>
        IsEnabled(environmentFlag, configEnabled);

    public static bool IsDemoSeedEnabled(IConfiguration config) =>
        IsDemoSeedEnabled(
            Environment.GetEnvironmentVariable("METERP_SEED_DEMO"),
            config.GetValue<bool>("Seed:Demo"));

    /// <summary><c>METERP_STARTUP_BACKFILL=true</c> or config <c>Seed:StartupBackfill=true</c>. Default false.</summary>
    public static bool IsStartupBackfillEnabled(string? environmentFlag, bool configEnabled) =>
        IsEnabled(environmentFlag, configEnabled);

    public static bool IsStartupBackfillEnabled(IConfiguration config) =>
        IsStartupBackfillEnabled(
            Environment.GetEnvironmentVariable("METERP_STARTUP_BACKFILL"),
            config.GetValue<bool>("Seed:StartupBackfill"));

    /// <summary><c>METERP_SEED_RESET=true</c> or config <c>Seed:ForceResetOnStart=true</c>.</summary>
    public static bool IsResetRequested(string? environmentFlag, bool configEnabled) =>
        IsEnabled(environmentFlag, configEnabled);

    public static bool IsResetRequested(IConfiguration config) =>
        IsResetRequested(
            Environment.GetEnvironmentVariable("METERP_SEED_RESET"),
            config.GetValue<bool>("Seed:ForceResetOnStart"));

    /// <summary>
    /// Reset is allowed only for an explicit demo database whose name is known and is not <c>METERP_Dev</c>.
    /// </summary>
    public static bool ShouldForceReset(bool demoSeedEnabled, string? databaseName)
    {
        if (!demoSeedEnabled || string.IsNullOrWhiteSpace(databaseName))
            return false;

        return !string.Equals(databaseName.Trim(), "METERP_Dev", StringComparison.OrdinalIgnoreCase);
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
