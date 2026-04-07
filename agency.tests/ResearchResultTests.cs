using ShareInvest.Agency.Models;

using System.Text.Json;

namespace ShareInvest.Agency.Tests;

public class ResearchResultTests
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    const string ValidV2Json = """
        {
          "schemaVersion": 2,
          "productData": [],
          "competitorInsights": [],
          "marketContext": "Growing market",
          "synthesizedInsights": "Strong product",
          "category": "Skincare",
          "coreValue": "Anti-aging",
          "keySellingPoints": ["Point 1", "Point 2"],
          "recommendedAngle": "Premium",
          "basis": "research"
        }
        """;

    [Fact]
    public void Deserializes_ValidV2Json()
    {
        var result = JsonSerializer.Deserialize<ResearchResult>(ValidV2Json, Options);

        Assert.NotNull(result);
        Assert.Equal(2, result.SchemaVersion);
        Assert.Equal("research", result.Basis);
        Assert.Equal("Skincare", result.Category);
    }

    [Fact]
    public void Deserializes_WithMissingSchemaVersion_ReturnsZero()
    {
        var json = """
            {
              "productData": [],
              "competitorInsights": [],
              "basis": "partial"
            }
            """;

        var result = JsonSerializer.Deserialize<ResearchResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(0, result.SchemaVersion); // v1 compat — caller should normalize to 2
        Assert.Equal("partial", result.Basis);
    }

    [Fact]
    public void Deserializes_WithNullBasis_BasisIsNull()
    {
        var json = """
            {
              "schemaVersion": 2,
              "productData": [],
              "competitorInsights": []
            }
            """;

        var result = JsonSerializer.Deserialize<ResearchResult>(json, Options);

        Assert.NotNull(result);
        Assert.Null(result.Basis);
    }

    [Fact]
    public void Deserializes_WithPartialFields_NullablesAreNull()
    {
        var json = """
            {
              "schemaVersion": 2,
              "productData": [],
              "competitorInsights": []
            }
            """;

        var result = JsonSerializer.Deserialize<ResearchResult>(json, Options);

        Assert.NotNull(result);
        Assert.Null(result.MarketContext);
        Assert.Null(result.Category);
        Assert.Null(result.CoreValue);
    }

    [Fact]
    public void Deserializes_InvalidJson_ReturnsNull_ViaException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ResearchResult>("not valid json", Options));
    }

    [Fact]
    public void SchemaVersion_DefaultsToZero_WhenAbsent()
    {
        // Verifies that the int field defaults to 0 (not 2) when absent from JSON
        var json = """{"productData":[],"competitorInsights":[]}""";
        var result = JsonSerializer.Deserialize<ResearchResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(0, result.SchemaVersion);
    }
}
