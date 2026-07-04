using METERP.Application.Options;
using Xunit;

namespace METERP.Application.Tests;

public class OtlpEndpointHelperTests
{
    [Theory]
    [InlineData("http://collector:4318", "traces", "http://collector:4318/v1/traces")]
    [InlineData("http://collector:4318/", "metrics", "http://collector:4318/v1/metrics")]
    [InlineData("http://collector:4318/v1/traces", "traces", "http://collector:4318/v1/traces")]
    public void BuildHttpProtobufEndpoint_AppendsSignalPath_WhenMissing(string endpoint, string signal, string expected)
    {
        var uri = OtlpEndpointHelper.BuildHttpProtobufEndpoint(endpoint, signal);

        Assert.Equal(expected, uri.ToString());
    }
}