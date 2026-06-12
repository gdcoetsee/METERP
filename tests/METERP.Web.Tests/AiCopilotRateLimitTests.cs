using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace METERP.Web.Tests;

public class AiCopilotRateLimitTests : IClassFixture<MeterpWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AiCopilotRateLimitTests(MeterpWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task AiCopilot_Path_ReturnsTooManyRequests_AfterPerMinuteLimit()
    {
        const int permitLimit = 30;
        HttpStatusCode? lastStatus = null;

        for (var i = 0; i < permitLimit + 1; i++)
        {
            var response = await _client.GetAsync("/ai-copilot");
            lastStatus = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    [Fact]
    public async Task Health_Endpoints_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}