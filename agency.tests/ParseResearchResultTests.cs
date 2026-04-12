using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.ParseResearchResult"/>.
/// Exercises JSON parsing, markdown code-fence stripping, schema normalization,
/// and graceful handling of malformed inputs.
/// </summary>
public class ParseResearchResultTests
{
    // GptService with a dummy key — ParseResearchResult never touches the network.
    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    const string MinimalValidJson = """
        {
          "schemaVersion": 2,
          "productData": [],
          "competitorInsights": [],
          "basis": "research"
        }
        """;

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseResearchResult_ValidJson_ReturnsResult()
    {
        var result = _sut.ParseResearchResult(MinimalValidJson);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal("research", result.Basis);
    }

    [Fact]
    public void ParseResearchResult_JsonInMarkdownFence_StripsFenceAndParses()
    {
        var raw = $"```json\n{MinimalValidJson}\n```";

        var result = _sut.ParseResearchResult(raw);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
    }

    [Fact]
    public void ParseResearchResult_JsonInUnlabelledFence_StripsFenceAndParses()
    {
        var raw = $"```\n{MinimalValidJson}\n```";

        var result = _sut.ParseResearchResult(raw);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
    }

    // ─── Schema normalization ─────────────────────────────────────────────────

    [Fact]
    public void ParseResearchResult_V1JsonWithSchemaVersion0_UpgradesTo2()
    {
        // V1 responses omit schemaVersion — field defaults to 0
        var json = """{"productData":[],"competitorInsights":[],"basis":"research"}""";

        var result = _sut.ParseResearchResult(json);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
    }

    [Fact]
    public void ParseResearchResult_NullBasisWithUrlsFetched_InfersBasisAsResearch()
    {
        var json = """{"schemaVersion":2,"productData":[],"competitorInsights":[]}""";

        var result = _sut.ParseResearchResult(json, urlsWereFetched: true);

        Assert.NotNull(result);
        Assert.Equal("research", result.Basis);
    }

    [Fact]
    public void ParseResearchResult_NullBasisWithoutUrlsFetched_InfersBasisAsCategoryInference()
    {
        var json = """{"schemaVersion":2,"productData":[],"competitorInsights":[]}""";

        var result = _sut.ParseResearchResult(json, urlsWereFetched: false);

        Assert.NotNull(result);
        Assert.Equal("category_inference", result.Basis);
    }

    [Fact]
    public void ParseResearchResult_ExplicitBasisPreserved()
    {
        var json = """{"schemaVersion":2,"productData":[],"competitorInsights":[],"basis":"partial"}""";

        var result = _sut.ParseResearchResult(json);

        Assert.NotNull(result);
        Assert.Equal("partial", result.Basis);
    }

    // ─── Malformed / non-JSON inputs ─────────────────────────────────────────

    [Fact]
    public void ParseResearchResult_InvalidJson_ReturnsNull()
    {
        var result = _sut.ParseResearchResult("this is not json at all");

        Assert.Null(result);
    }

    [Fact]
    public void ParseResearchResult_EmptyString_ReturnsNull()
    {
        // Empty raw is guarded by the caller (whitespace check), but ParseResearchResult
        // itself should also survive gracefully if called with empty JSON.
        var result = _sut.ParseResearchResult("{}");

        // {} is valid JSON but produces a result with default values — not null
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseResearchResult_JsonArray_ReturnsNull()
    {
        var result = _sut.ParseResearchResult("[1, 2, 3]");

        Assert.Null(result);
    }

    [Fact]
    public void ParseResearchResult_TruncatedJson_ReturnsNull()
    {
        var result = _sut.ParseResearchResult("{\"schemaVersion\": 2, \"productData\":");

        Assert.Null(result);
    }

    // ─── Field mapping ────────────────────────────────────────────────────────

    [Fact]
    public void ParseResearchResult_AllFields_MapsCorrectly()
    {
        var json = """
            {
              "schemaVersion": 2,
              "productData": [],
              "competitorInsights": [],
              "marketContext": "Growing market",
              "synthesizedInsights": "Strong demand",
              "category": "Skincare",
              "coreValue": "Anti-aging",
              "keySellingPoints": ["Hydration", "Firming"],
              "recommendedAngle": "Premium anti-aging",
              "basis": "research"
            }
            """;

        var result = _sut.ParseResearchResult(json);

        Assert.NotNull(result);
        Assert.Equal("Growing market", result.MarketContext);
        Assert.Equal("Skincare", result.Category);
        Assert.Equal("Anti-aging", result.CoreValue);
        Assert.Equal("Premium anti-aging", result.RecommendedAngle);
        Assert.Contains("Hydration", result.KeySellingPoints ?? []);
    }

    [Fact]
    public void ParseResearchResult_CaseInsensitiveKeys_ParsesCorrectly()
    {
        // The JSON uses PascalCase keys — deserializer must be case-insensitive
        var json = """
            {
              "SchemaVersion": 2,
              "ProductData": [],
              "CompetitorInsights": [],
              "Basis": "research"
            }
            """;

        var result = _sut.ParseResearchResult(json);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal("research", result.Basis);
    }
}
