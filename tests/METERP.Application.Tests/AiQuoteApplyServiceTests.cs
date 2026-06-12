using METERP.Application.Services;
using METERP.Domain;
using METERP.Infrastructure.Services;
using Moq;
using Xunit;

namespace METERP.Application.Tests;

public class AiQuoteApplyServiceTests
{
    private readonly Mock<IQuoteService> _quoteService = new();
    private readonly Mock<IAiAssistantService> _aiService = new();

    private AiQuoteApplyService CreateService() =>
        new(_quoteService.Object, _aiService.Object);

    [Fact]
    public async Task CreateQuoteFromAiTextAsync_AddsSuggestedLines_WhenAiReturnsStructuredSuggestion()
    {
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var createdQuote = new Quote { Id = quoteId, CustomerId = customerId, TaxRate = 0.15m };

        _quoteService.Setup(s => s.CreateAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteId);
        _aiService.Setup(s => s.SuggestQuoteLinesAsync(It.IsAny<string>(), 0.15m, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiQuoteSuggestion("ok", new List<AiSuggestedLine>
            {
                new("DB board install", 1, "ea", "Material", 2500m),
                new("Travel to remote site", 1, "lot", "Other", 720m)
            }));
        _quoteService.Setup(s => s.GetByIdAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdQuote);

        var service = CreateService();
        var result = await service.CreateQuoteFromAiTextAsync("Install transformer at mine site", customerId);

        Assert.Equal(quoteId, result.Id);
        _quoteService.Verify(s => s.AddLineAsync(It.Is<QuoteLine>(l =>
            l.Description == "DB board install" && l.QuoteId == quoteId), It.IsAny<CancellationToken>()), Times.Once);
        _quoteService.Verify(s => s.AddLineAsync(It.Is<QuoteLine>(l =>
            l.Description == "Travel to remote site"), It.IsAny<CancellationToken>()), Times.Once);
        _quoteService.Verify(s => s.CreateAsync(It.Is<Quote>(q =>
            q.CustomerId == customerId && (q.Notes ?? "").Contains("AI Generated:")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateQuoteFromAiTextAsync_AddsFallbackTravelLine_WhenSuggestionIsNull()
    {
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();

        _quoteService.Setup(s => s.CreateAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteId);
        _aiService.Setup(s => s.SuggestQuoteLinesAsync(It.IsAny<string>(), It.IsAny<decimal>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiQuoteSuggestion?)null);
        _quoteService.Setup(s => s.GetByIdAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote { Id = quoteId, CustomerId = customerId });

        await CreateService().CreateQuoteFromAiTextAsync("scope with travel", customerId);

        _quoteService.Verify(s => s.AddLineAsync(It.Is<QuoteLine>(l =>
            l.QuoteId == quoteId &&
            l.Description.Contains("Travel", StringComparison.OrdinalIgnoreCase) &&
            l.UnitPrice == 650m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateQuoteFromAiTextAsync_AddsFallbackTravelLine_WhenSuggestionThrows()
    {
        var customerId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();

        _quoteService.Setup(s => s.CreateAsync(It.IsAny<Quote>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(quoteId);
        _aiService.Setup(s => s.SuggestQuoteLinesAsync(It.IsAny<string>(), It.IsAny<decimal>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI unavailable"));
        _quoteService.Setup(s => s.GetByIdAsync(quoteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote { Id = quoteId, CustomerId = customerId });

        await CreateService().CreateQuoteFromAiTextAsync("scope", customerId);

        _quoteService.Verify(s => s.AddLineAsync(It.Is<QuoteLine>(l =>
            l.Description.Contains("Travel", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateQuoteFromAiTextAsync_Throws_WhenCustomerMissing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateQuoteFromAiTextAsync("text", Guid.Empty));
    }

    [Fact]
    public async Task CreateQuoteFromAiTextAsync_Throws_WhenTextEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateQuoteFromAiTextAsync("  ", Guid.NewGuid()));
    }
}