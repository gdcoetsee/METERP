using METERP.Infrastructure.Seeding;
using Xunit;

namespace METERP.Application.Tests;

public class SeedProfileGatesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    public void IsExplicitlyEnabled_AcceptsTrueOneYes(string? flag, bool expected) =>
        Assert.Equal(expected, SeedProfileGates.IsExplicitlyEnabled(flag));

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData(null, true, true)]
    [InlineData("   ", true, true)]
    [InlineData("false", true, false)]
    [InlineData("true", false, true)]
    [InlineData("0", true, false)]
    public void IsEnabled_EnvironmentWinsWhenSet(string? environmentFlag, bool configEnabled, bool expected) =>
        Assert.Equal(expected, SeedProfileGates.IsEnabled(environmentFlag, configEnabled));

    [Fact]
    public void DemoAndBackfill_DefaultOff_AndShareTheSameFlagRules()
    {
        Assert.False(SeedProfileGates.IsDemoSeedEnabled(null, false));
        Assert.False(SeedProfileGates.IsStartupBackfillEnabled(null, false));
        Assert.True(SeedProfileGates.IsDemoSeedEnabled(null, true));
        Assert.True(SeedProfileGates.IsStartupBackfillEnabled("true", false));
        Assert.False(SeedProfileGates.IsDemoSeedEnabled("false", true));
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(false, "METERP", false)]
    [InlineData(false, "METERP_Dev", false)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(true, "METERP_Dev", false)]
    [InlineData(true, "meterp_dev", false)]
    [InlineData(true, " METERP_Dev ", false)]
    [InlineData(true, "METERP", true)]
    [InlineData(true, "meterp_ci", true)]
    public void ShouldForceReset_RequiresDemoSeedAndANonDevDatabase(bool demoSeed, string? databaseName, bool expected) =>
        Assert.Equal(expected, SeedProfileGates.ShouldForceReset(demoSeed, databaseName));

    [Fact]
    public void IsResetRequested_UsesForceResetConfigWhenEnvMissing()
    {
        Assert.False(SeedProfileGates.IsResetRequested(null, false));
        Assert.True(SeedProfileGates.IsResetRequested(null, true));
        Assert.False(SeedProfileGates.IsResetRequested("false", true));
    }
}
