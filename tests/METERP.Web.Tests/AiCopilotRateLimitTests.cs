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

    [Fact]
    public async Task BlazorCircuit_Endpoints_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var negotiate = await _client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, negotiate.StatusCode);

            var circuit = await _client.GetAsync("/_blazor");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, circuit.StatusCode);
        }
    }

    [Fact]
    public async Task E2e_Endpoints_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.PostAsync("/e2e/rate-limit-probe", null);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Webhook_Endpoints_AreNotRateLimited()
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        for (var i = 0; i < 35; i++)
        {
            var response = await _client.PostAsync("/webhooks/stripe", content);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Framework_Static_Assets_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/_framework/blazor.boot.json");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task LoginComplete_Path_IsNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/login-complete");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Account_Path_IsNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/account");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Approvals_Path_IsNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/approvals");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Login_Path_IsNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var response = await _client.GetAsync("/login");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Quotes_And_Jobs_Paths_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var quotes = await _client.GetAsync("/quotes");
            var jobs = await _client.GetAsync("/jobs");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, quotes.StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, jobs.StatusCode);
        }
    }

    [Fact]
    public async Task Invoices_And_Opportunities_Paths_AreNotRateLimited()
    {
        for (var i = 0; i < 35; i++)
        {
            var invoices = await _client.GetAsync("/invoices");
            var opportunities = await _client.GetAsync("/opportunities");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, invoices.StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, opportunities.StatusCode);
        }
    }
}