using METERP.Domain;
using Xunit;

namespace METERP.Application.Tests;

public class TravelLineRulesTests
{
    [Theory]
    [InlineData("Travel", "Cable", "Travel")]
    [InlineData("Material", "Travel to site", "Travel")]
    [InlineData("Other", "Site travel and fuel", "Travel")]
    [InlineData("Labour", "Travel time on site", "Labour")]
    [InlineData("Material", "DB board", "Material")]
    [InlineData(null, "Travel", "Travel")]
    public void EnsureExplicitType_KeepsTravelOutOfMaterials(string? lineType, string description, string expected)
    {
        Assert.Equal(expected, TravelLineRules.EnsureExplicitType(lineType, description));
    }

    [Fact]
    public void JobCostType_NeverReturnsMaterialForTravel()
    {
        Assert.Equal("Travel", TravelLineRules.JobCostType("Material", "Travel to site"));
        Assert.Equal("Travel", TravelLineRules.JobCostType("Travel", "Mobilisation"));
        Assert.Equal("Material", TravelLineRules.JobCostType("Material", "Cable drums"));
    }
}
