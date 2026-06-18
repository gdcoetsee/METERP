using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Common;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

/// <summary>
/// OpenAI-compatible AI assistant (works with OpenAI, xAI/Grok, Groq, Ollama, Azure, etc.).
/// Designed to be optional and never break the ERP if the LLM is unavailable.
/// </summary>
public class AiAssistantService : IAiAssistantService
{
    private readonly IAiConfigurationResolver _configResolver;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiAssistantService> _logger;
    private readonly ITenantService? _tenantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly IQuotaService? _quotaService;

    public bool IsConfigured => _configResolver.IsDeploymentConfigured;

    private static readonly ConcurrentDictionary<string, DateTime> _lastAiCall = new();
    private static readonly TimeSpan _minAiInterval = TimeSpan.FromSeconds(4);

    internal static void ClearThrottleStateForTesting() => _lastAiCall.Clear();

    public AiAssistantService(
        IAiConfigurationResolver configResolver,
        ILogger<AiAssistantService> logger,
        ITenantService? tenantService = null,
        ITenantProvider? tenantProvider = null,
        IQuotaService? quotaService = null,
        HttpClient? httpClient = null)
    {
        _configResolver = configResolver;
        _logger = logger;
        _tenantService = tenantService;
        _tenantProvider = tenantProvider;
        _quotaService = quotaService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<AiQuoteSuggestion?> SuggestQuoteLinesAsync(
        string scopeNotes,
        decimal taxRate,
        string? customerName = null,
        CancellationToken ct = default)
    {
        var config = await _configResolver.GetEffectiveAsync(ct);
        if (!config.IsConfigured || string.IsNullOrWhiteSpace(scopeNotes))
            return null;

        if (!IsAiCallAllowed())
        {
            _logger.LogWarning("AI call throttled (rate limit)");
            return null;
        }

        if (!await IsAiFeatureEnabledAsync(ct))
            return null;

        var quotaMessage = await CheckAiQuotaAsync(ct);
        if (quotaMessage != null)
        {
            _logger.LogInformation("AI quote suggestion blocked: {Reason}", quotaMessage);
            return null;
        }

        try
        {
            var system = """
                You are an expert quoting assistant for a South African electrical and mechanical contracting company (METERP ERP).
                Always think in South African Rand (R). Factor in realistic travel/transport costs (they are always tracked separately).
                Prefer materials that would come from inventory. Use practical quantities and current SA market rates.
                Respond ONLY with a single JSON object matching this exact shape (no markdown, no extra text):

                {
                  "reasoning": "short explanation of how you built the estimate",
                  "suggestedLines": [
                    {
                      "description": "...",
                      "quantity": 1.0,
                      "unit": "ea|hr|m|etc",
                      "lineType": "Material|Labour|Other",
                      "unitPrice": 123.45,
                      "suggestedInventorySku": "SKU-123 or null",
                      "notes": "optional"
                    }
                  ]
                }
                """;

            var user = $"""
                Customer: {customerName ?? "Not specified"}
                Tax/VAT rate: {(taxRate * 100):0.##}%
                Job/Scope notes:
                {scopeNotes}

                Suggest a realistic, complete set of line items for the quote. Include labour, materials, and at least one Travel line if site work is implied.
                """;

            var payload = BuildChatPayload(config, new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }, temperature: 0.3, jsonObject: true);

            var respJson = await PostChatCompletionsAsync(config, payload, ct);
            using var doc = JsonDocument.Parse(respJson);

            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
                return null;

            await TryIncrementAiCallCountAsync(ct);
            return AiQuoteSuggestionParser.TryParse(messageContent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI quote suggestion failed");
            return null;
        }
    }

    public async Task<AiJobAnalysis?> AnalyzeJobVarianceAsync(Job job, CancellationToken ct = default)
    {
        var config = await _configResolver.GetEffectiveAsync(ct);
        if (!config.IsConfigured || job == null)
            return null;

        if (!IsAiCallAllowed())
        {
            _logger.LogWarning("AI call throttled (rate limit)");
            return null;
        }

        if (!await IsAiFeatureEnabledAsync(ct))
            return null;

        var quotaMessage = await CheckAiQuotaAsync(ct);
        if (quotaMessage != null)
        {
            _logger.LogInformation("AI job analysis blocked for job {JobId}: {Reason}", job.Id, quotaMessage);
            return null;
        }

        try
        {
            var laborTotal = job.Labors?.Where(l => !l.IsDeleted).Sum(l => l.TotalCost) ?? 0;
            var actualTotal = job.ActualCost + laborTotal;

            var costBreakdown = "";
            if (job.ActualCosts != null)
            {
                var groups = job.ActualCosts
                    .Where(c => !c.IsDeleted)
                    .GroupBy(c => c.CostType)
                    .Select(g => $"{g.Key}: R {g.Sum(x => x.Amount):N2}");
                costBreakdown = string.Join(", ", groups);
            }

            var system = """
                You are a senior project analyst for a South African electrical & mechanical contracting firm using METERP.
                You are brutally honest but constructive. You always explicitly call out Travel costs.
                Output ONLY a single JSON object with this exact structure (no extra text, no markdown fences):

                {
                  "summary": "one paragraph executive summary of the job outcome",
                  "varianceDrivers": "detailed but concise analysis of why the job was over/under budget (materials, labour, travel, etc.)",
                  "recommendations": ["actionable bullet 1", "actionable bullet 2", "..."],
                  "suggestedMarginNote": "optional short note that could be added to future quotes or job notes"
                }
                """;

            var user = $"""
                Job: {job.JobNumber} - {job.Title}
                Customer: {job.Customer?.Name ?? "Unknown"}
                Quoted Total: R {job.QuotedTotal:N2}
                Actual Materials: R {job.ActualCost:N2}
                Actual Labor: R {laborTotal:N2}
                Total Actual: R {actualTotal:N2}
                Variance: R {actualTotal - job.QuotedTotal:N2}

                Cost breakdown by type: {costBreakdown}

                Labor entries: {(job.Labors?.Count(l => !l.IsDeleted) ?? 0)} lines
                Linked Asset: {job.Asset?.Name ?? "None"}

                Notes / Scope:
                {job.Notes ?? job.Description ?? "(none provided)"}

                Analyze the variance. Highlight travel specifically. Give 2-4 concrete, actionable recommendations the team can use on the next similar job.
                """;

            var payload = BuildChatPayload(config, new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }, temperature: 0.25, jsonObject: true);

            var respJson = await PostChatCompletionsAsync(config, payload, ct);
            using var doc = JsonDocument.Parse(respJson);

            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
                return null;

            using var analysisDoc = JsonDocument.Parse(messageContent);

            var recommendations = new List<string>();
            if (analysisDoc.RootElement.TryGetProperty("recommendations", out var recArr) && recArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in recArr.EnumerateArray())
                    recommendations.Add(r.GetString() ?? "");
            }

            await TryIncrementAiCallCountAsync(ct);

            return new AiJobAnalysis(
                analysisDoc.RootElement.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                analysisDoc.RootElement.TryGetProperty("varianceDrivers", out var v) ? v.GetString() ?? "" : "",
                recommendations,
                analysisDoc.RootElement.TryGetProperty("suggestedMarginNote", out var m) ? m.GetString() : null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI job variance analysis failed for job {JobId}", job.Id);
            return null;
        }
    }

    public async Task<string?> AskCopilotAsync(string question, string? additionalContext = null, CancellationToken ct = default)
    {
        var config = await _configResolver.GetEffectiveAsync(ct);
        if (!config.IsConfigured || string.IsNullOrWhiteSpace(question))
            return null;

        if (!IsAiCallAllowed())
        {
            _logger.LogWarning("AI call throttled (rate limit)");
            return "Rate limit reached for AI assistant. Please wait a moment before asking again.";
        }

        if (!await IsAiFeatureEnabledAsync(ct))
            return "AI Copilot feature is not enabled for your tenant (tier or configuration). Contact admin to enable.";

        var quotaMessage = await CheckAiQuotaAsync(ct);
        if (quotaMessage != null)
            return quotaMessage;

        try
        {
            var system = """
                You are an expert AI co-pilot for METERP, a South African electrical and mechanical contracting ERP.
                You have deep knowledge of job costing (including mandatory travel costs), inventory, quoting with 15% VAT, labor utilization, asset maintenance, and multi-tenant operations.
                Be practical, data-driven, and actionable. Always speak in Rands and reference South African contracting realities (mines, sites, travel, skills shortages, etc.).
                If the user asks for an estimate or suggestion, be realistic and factor in buffers.
                Return concise, professional responses. Use bullet points and clear sections when helpful.
                """;

            var user = $"Question: {question}\n\nAdditional current context from the system:\n{additionalContext ?? "(none)"}";

            var payload = BuildChatPayload(config, new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }, temperature: 0.4, maxTokens: 800);

            var respJson = await PostChatCompletionsAsync(config, payload, ct);
            using var doc = JsonDocument.Parse(respJson);

            var responseText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            await TryIncrementAiCallCountAsync(ct);
            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Copilot failed");
            return ex.Message.StartsWith("AI API", StringComparison.Ordinal)
                ? $"AI Error: {ex.Message}"
                : $"AI Error: {ex.Message}";
        }
    }

    private async Task<string> PostChatCompletionsAsync(AiRuntimeConfiguration config, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = content
        };
        AiHttpAuth.ApplyApiKey(request, config.ProviderName, config.ApiKey!);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

        using var resp = await _httpClient.SendAsync(request, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "AI API error from {Provider} ({Status}): {Body}",
                config.ProviderName,
                resp.StatusCode,
                body);
            throw new HttpRequestException(
                TenantAiSettingsService.FormatApiError(resp.StatusCode, body, config.ProviderName));
        }

        return body;
    }

    private static object BuildChatPayload(
        AiRuntimeConfiguration config,
        object[] messages,
        double temperature,
        bool jsonObject = false,
        int? maxTokens = null)
    {
        if (jsonObject && SupportsJsonObjectResponseFormat(config.ProviderName))
        {
            if (maxTokens.HasValue)
            {
                return new
                {
                    model = config.Model,
                    messages,
                    temperature,
                    max_tokens = maxTokens.Value,
                    response_format = new { type = "json_object" }
                };
            }

            return new
            {
                model = config.Model,
                messages,
                temperature,
                response_format = new { type = "json_object" }
            };
        }

        if (maxTokens.HasValue)
            return new { model = config.Model, messages, temperature, max_tokens = maxTokens.Value };

        return new { model = config.Model, messages, temperature };
    }

    private static bool SupportsJsonObjectResponseFormat(string provider) =>
        !string.Equals(provider, AiProviderProfiles.GoogleGemini, StringComparison.Ordinal)
        && !string.Equals(provider, AiProviderProfiles.Ollama, StringComparison.Ordinal);

    private bool IsAiCallAllowed()
    {
        var tenantKey = _tenantProvider?.GetCurrentTenantId() is Guid tid && tid != Guid.Empty
            ? tid.ToString()
            : "global";

        var now = DateTime.UtcNow;
        if (_lastAiCall.TryGetValue(tenantKey, out var last) && (now - last) < _minAiInterval)
            return false;

        _lastAiCall[tenantKey] = now;
        return true;
    }

    private async Task<bool> IsAiFeatureEnabledAsync(CancellationToken ct)
    {
        try
        {
            var tid = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
            if (tid == Guid.Empty || _tenantService == null)
                return true;

            var tenant = await _tenantService.GetByIdAsync(tid, ct);
            if (tenant != null && !tenant.HasFeature("ai"))
            {
                _logger.LogInformation("AI feature disabled for tenant {TenantId}", tid);
                return false;
            }
        }
        catch { /* non-fatal */ }

        return true;
    }

    private async Task<string?> CheckAiQuotaAsync(CancellationToken ct)
    {
        var tid = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (tid == Guid.Empty || _quotaService == null) return null;

        try
        {
            await _quotaService.EnsureAllowedAsync(tid, QuotaType.AiCall, ct);
            return null;
        }
        catch (QuotaExceededException ex)
        {
            return ex.Message;
        }
    }

    private async Task TryIncrementAiCallCountAsync(CancellationToken ct)
    {
        var tid = _tenantProvider?.GetCurrentTenantId() ?? Guid.Empty;
        if (tid == Guid.Empty || _tenantService == null) return;
        try
        {
            await _tenantService.IncrementAiCallCountAsync(tid, ct);
        }
        catch
        {
            // Best-effort commercial tracking — must not break AI flows.
        }
    }
}