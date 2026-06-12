using METERP.Application.Interfaces;
using METERP.Application.Services;
using METERP.Domain;

namespace METERP.Infrastructure.Services;

/// <summary>
/// Applies AI copilot responses to quotes using QuoteService + optional structured line suggestions.
/// </summary>
public class AiQuoteApplyService : IAiQuoteApplyService
{
    private const string FallbackTravelDescription = "Travel & site transport (AI estimate)";
    private const decimal FallbackTravelUnitPrice = 650m;

    private readonly IQuoteService _quoteService;
    private readonly IAiAssistantService _aiAssistantService;
    private readonly ITenantProvider? _tenantProvider;
    private readonly ITenantService? _tenantService;

    public AiQuoteApplyService(
        IQuoteService quoteService,
        IAiAssistantService aiAssistantService,
        ITenantProvider? tenantProvider = null,
        ITenantService? tenantService = null)
    {
        _quoteService = quoteService;
        _aiAssistantService = aiAssistantService;
        _tenantProvider = tenantProvider;
        _tenantService = tenantService;
    }

    public async Task<Quote> CreateQuoteFromAiTextAsync(
        string aiText,
        Guid customerId,
        decimal taxRate = 0.15m,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aiText))
            throw new ArgumentException("AI text is required.", nameof(aiText));

        if (customerId == Guid.Empty)
            throw new ArgumentException("A customer is required.", nameof(customerId));

        await AiCopilotAccessGuard.EnsureAiApplyAllowedAsync(_tenantProvider, _tenantService, ct);

        var newQuote = new Quote
        {
            QuoteDate = DateTime.UtcNow,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            TaxRate = taxRate,
            Notes = "AI Generated: " + aiText,
            CustomerId = customerId
        };

        var createdId = await _quoteService.CreateAsync(newQuote, ct);

        try
        {
            var suggestion = await _aiAssistantService.SuggestQuoteLinesAsync(aiText, taxRate, null, ct);
            if (suggestion?.SuggestedLines?.Any() == true)
            {
                foreach (var s in suggestion.SuggestedLines.Take(6))
                {
                    await _quoteService.AddLineAsync(new QuoteLine
                    {
                        QuoteId = createdId,
                        Description = s.Description,
                        Quantity = s.Quantity,
                        UnitPrice = s.UnitPrice,
                        Unit = s.Unit,
                        LineType = s.LineType
                    }, ct);
                }
            }
            else
            {
                await AddFallbackTravelLineAsync(createdId, ct);
            }
        }
        catch
        {
            // Non-fatal: quote shell exists; ensure explicit travel for contractor flows.
            await AddFallbackTravelLineAsync(createdId, ct);
        }

        return (await _quoteService.GetByIdAsync(createdId, ct))!;
    }

    private Task AddFallbackTravelLineAsync(Guid quoteId, CancellationToken ct) =>
        _quoteService.AddLineAsync(new QuoteLine
        {
            QuoteId = quoteId,
            Description = FallbackTravelDescription,
            Quantity = 1,
            UnitPrice = FallbackTravelUnitPrice,
            LineType = "Other",
            Unit = "lot"
        }, ct);
}