using METERP.Application.Services;
using Xunit;

namespace METERP.Application.Tests;

public class AiQuoteSuggestionParserTests
{
    [Fact]
    public void TryParse_ReturnsStructuredSuggestion_FromValidJson()
    {
        const string json = """
            {
              "reasoning": "Included travel for remote site work.",
              "suggestedLines": [
                {
                  "description": "DB board install",
                  "quantity": 2,
                  "unit": "ea",
                  "lineType": "Material",
                  "unitPrice": 2500,
                  "suggestedInventorySku": "DB-100",
                  "notes": "Main distribution"
                },
                {
                  "description": "Travel to site",
                  "quantity": 1,
                  "unit": "lot",
                  "lineType": "Other",
                  "unitPrice": 720
                }
              ]
            }
            """;

        var result = AiQuoteSuggestionParser.TryParse(json);

        Assert.NotNull(result);
        Assert.Equal("Included travel for remote site work.", result.Reasoning);
        Assert.Equal(2, result.SuggestedLines.Count);
        Assert.Equal("DB board install", result.SuggestedLines[0].Description);
        Assert.Equal(2m, result.SuggestedLines[0].Quantity);
        Assert.Equal("DB-100", result.SuggestedLines[0].SuggestedInventorySku);
        Assert.Equal("Travel to site", result.SuggestedLines[1].Description);
        Assert.Equal(720m, result.SuggestedLines[1].UnitPrice);
    }

    [Fact]
    public void TryParse_AppliesDefaults_ForMissingOptionalFields()
    {
        const string json = """
            {
              "suggestedLines": [
                { "description": "Labour only" }
              ]
            }
            """;

        var result = AiQuoteSuggestionParser.TryParse(json);

        Assert.NotNull(result);
        Assert.Equal("", result.Reasoning);
        var line = Assert.Single(result.SuggestedLines);
        Assert.Equal("Labour only", line.Description);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("ea", line.Unit);
        Assert.Equal("Other", line.LineType);
        Assert.Equal(0m, line.UnitPrice);
        Assert.Null(line.SuggestedInventorySku);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForInvalidJson()
    {
        Assert.Null(AiQuoteSuggestionParser.TryParse("not json"));
        Assert.Null(AiQuoteSuggestionParser.TryParse(null));
        Assert.Null(AiQuoteSuggestionParser.TryParse("   "));
    }

    [Fact]
    public void TryParse_ReturnsEmptyLines_WhenSuggestedLinesMissing()
    {
        const string json = """{ "reasoning": "No lines yet" }""";

        var result = AiQuoteSuggestionParser.TryParse(json);

        Assert.NotNull(result);
        Assert.Equal("No lines yet", result.Reasoning);
        Assert.Empty(result.SuggestedLines);
    }
}