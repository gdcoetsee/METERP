using METERP.Common;
using Xunit;

namespace METERP.Application.Tests;

public class OfficeReferenceTokensTests
{
    [Fact]
    public void Extract_FindsMetTrfids_AndIgnoresNoise()
    {
        var tokens = OfficeReferenceTokens.Extract("How is ft16010 versus SD393 and the PD0085 panel? Not FT or invoice 12.");

        Assert.Equal(new[] { "FT16010", "SD393", "PD0085" }, tokens);
    }

    [Fact]
    public void Extract_ReturnsEmpty_WhenTextHasNoTrfid()
    {
        Assert.Empty(OfficeReferenceTokens.Extract("What travel cost risks should I watch?"));
    }
}
