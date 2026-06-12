using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

/// <summary>
/// Full-path AiAssistantService tests with mocked LLM HTTP responses.
/// </summary>
public class AiAssistantServiceHttpTests
{
    public AiAssistantServiceHttpTests() => AiAssistantService.ClearThrottleStateForTesting();

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:ApiKey"] = "test-key",
                ["Ai:BaseUrl"] = "https://llm.test/v1",
                ["Ai:Model"] = "gpt-test",
                ["Ai:Enabled"] = "true",
                ["Ai:TimeoutSeconds"] = "30"
            })
            .Build();

    private static ILogger<AiAssistantService> CreateLogger() =>
        Mock.Of<ILogger<AiAssistantService>>();

    private static HttpClient CreateMockLlmClient(string llmContent) =>
        CreateMockLlmClientFromEnvelope(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = llmContent } } }
        }));

    private static HttpClient CreateMockLlmClientFromEnvelope(string envelope)
    {
        var handler = new StubLlmHandler(envelope);
        return new HttpClient(handler) { BaseAddress = new Uri("https://llm.test/v1/") };
    }

    private static string QuoteSuggestionContent => JsonSerializer.Serialize(new
    {
        reasoning = "Included travel for site work.",
        suggestedLines = new[]
        {
            new
            {
                description = "Travel to remote site",
                quantity = 1m,
                unit = "lot",
                lineType = "Other",
                unitPrice = 720m
            }
        }
    });

    private static AiAssistantService CreateEnabledService(
        ITenantService tenantService,
        ITenantProvider tenantProvider,
        HttpClient http,
        IQuotaService? quotaService = null) =>
        new(
            CreateConfig(),
            CreateLogger(),
            tenantService,
            tenantProvider,
            quotaService,
            http);

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsParsedSuggestion_When_LlmReturnsValidJson()
    {
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        tenantService.Setup(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        using var http = CreateMockLlmClient(QuoteSuggestionContent);
        var service = CreateEnabledService(tenantService.Object, tenantProvider.Object, http);

        var result = await service.SuggestQuoteLinesAsync("Install transformer with travel", 0.15m, "Acme Mining");

        Assert.NotNull(result);
        Assert.Contains("travel", result.Reasoning, StringComparison.OrdinalIgnoreCase);
        var line = Assert.Single(result.SuggestedLines);
        Assert.Equal("Travel to remote site", line.Description);
        Assert.Equal(720m, line.UnitPrice);
        tenantService.Verify(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SuggestQuoteLinesAsync_ReturnsNull_When_QuotaExceeded()
    {
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai" });

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var quotaService = new Mock<IQuotaService>();
        quotaService.Setup(q => q.EnsureAllowedAsync(tenantId, QuotaType.AiCall, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QuotaExceededException(QuotaType.AiCall, 100, 100));

        using var http = CreateMockLlmClient(QuoteSuggestionContent);
        var service = CreateEnabledService(tenantService.Object, tenantProvider.Object, http, quotaService.Object);

        var result = await service.SuggestQuoteLinesAsync("scope", 0.15m);

        Assert.Null(result);
        tenantService.Verify(s => s.IncrementAiCallCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnalyzeJobVarianceAsync_ReturnsParsedAnalysis_When_LlmReturnsValidJson()
    {
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        tenantService.Setup(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        var analysisContent = JsonSerializer.Serialize(new
        {
            summary = "Job ran over budget primarily on travel.",
            varianceDrivers = "Travel allowance exceeded quoted amount.",
            recommendations = new[] { "Add 15% travel buffer", "Confirm site access before quoting" },
            suggestedMarginNote = "Include explicit travel line on similar bids"
        });

        using var http = CreateMockLlmClient(analysisContent);
        var service = CreateEnabledService(tenantService.Object, tenantProvider.Object, http);

        var job = new Job
        {
            JobNumber = "J-2026-TEST",
            Title = "Transformer install",
            QuotedTotal = 10000m,
            ActualCost = 8500m,
            ActualCosts = new List<JobCost>
            {
                new() { CostType = "Travel", Amount = 2200m, Description = "Site travel" }
            }
        };

        var result = await service.AnalyzeJobVarianceAsync(job);

        Assert.NotNull(result);
        Assert.Contains("travel", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Recommendations.Count);
        Assert.Contains("travel buffer", result.Recommendations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Include explicit travel line on similar bids", result.SuggestedMarginNote);
        tenantService.Verify(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskCopilotAsync_ReturnsResponseText_When_LlmSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var tenantService = new Mock<ITenantService>();
        tenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, EnabledFeatures = "ai,usage-tracking" });
        tenantService.Setup(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(p => p.GetCurrentTenantId()).Returns(tenantId);

        const string copilotReply = "Your top variance driver this month is explicit Travel costs on remote site jobs.";
        using var http = CreateMockLlmClient(copilotReply);
        var service = CreateEnabledService(tenantService.Object, tenantProvider.Object, http);

        var result = await service.AskCopilotAsync("What are my top cost risks?");

        Assert.Equal(copilotReply, result);
        tenantService.Verify(s => s.IncrementAiCallCountAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class StubLlmHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubLlmHandler(string responseBody) => _responseBody = responseBody;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}