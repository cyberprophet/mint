using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.ParseProductInfoResult"/> — the JSON parsing +
/// provenance-validation path exercised without network I/O. Validates the Intent 027
/// contract: strict JSON, nullable fields, per-field sourceDocument attribution, and
/// no-hallucination enforcement (unknown document ids are dropped).
/// </summary>
public class ParseProductInfoResultTests
{
    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    static readonly IReadOnlyList<ProductInfoDocument> TwoDocs =
    [
        new("spec.pdf", "ignored body"),
        new("marketing.md", "ignored body")
    ];

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_MinimalValidJson_ReturnsResult()
    {
        var json = """
            {
              "schemaVersion": 1,
              "productName": { "value": "Acme Widget", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf", "marketing.md"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json, TwoDocs);

        Assert.NotNull(result);
        Assert.Equal(1, result.SchemaVersion);
        Assert.NotNull(result.ProductName);
        Assert.Equal("Acme Widget", result.ProductName.Value);
        Assert.Equal("spec.pdf", result.ProductName.SourceDocument);
    }

    [Fact]
    public void Parse_AllFieldsPopulated_MapsCorrectly()
    {
        var json = """
            {
              "schemaVersion": 1,
              "productName":     { "value": "Acme Widget",                  "sourceDocument": "spec.pdf" },
              "oneLiner":        { "value": "The quiet revolution.",         "sourceDocument": "marketing.md" },
              "keyFeatures":     { "value": ["fast","quiet","durable"],      "sourceDocument": "spec.pdf" },
              "detailedSpec":    { "value": "20cm x 30cm, 1.2kg",             "sourceDocument": "spec.pdf" },
              "usage":           { "value": "Place on flat surface.",        "sourceDocument": "spec.pdf" },
              "cautions":        { "value": "Do not immerse.",               "sourceDocument": "spec.pdf" },
              "targetCustomer":  { "value": "Home office professionals",     "sourceDocument": "marketing.md" },
              "sellingPoints":   { "value": ["Best-in-class","Award-winning"],"sourceDocument": "marketing.md" },
              "sourceDocuments": ["spec.pdf","marketing.md"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json, TwoDocs);

        Assert.NotNull(result);
        Assert.Equal("Acme Widget", result.ProductName!.Value);
        Assert.Equal("marketing.md", result.OneLiner!.SourceDocument);
        Assert.Equal(3, result.KeyFeatures!.Value.Length);
        Assert.Contains("quiet", result.KeyFeatures.Value);
        Assert.Equal("spec.pdf", result.DetailedSpec!.SourceDocument);
        Assert.Equal("Home office professionals", result.TargetCustomer!.Value);
        Assert.Equal(2, result.SellingPoints!.Value.Length);
        Assert.Equal(["spec.pdf", "marketing.md"], result.SourceDocuments);
    }

    // ─── Null-field / no-hallucination contract ──────────────────────────────

    [Fact]
    public void Parse_FieldAbsent_ReturnsNullForThatField()
    {
        // Only productName is present — every other field must come back null
        var json = """
            {
              "schemaVersion": 1,
              "productName": { "value": "Acme", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json, [new("spec.pdf", "x")]);

        Assert.NotNull(result);
        Assert.NotNull(result.ProductName);
        Assert.Null(result.OneLiner);
        Assert.Null(result.KeyFeatures);
        Assert.Null(result.DetailedSpec);
        Assert.Null(result.Usage);
        Assert.Null(result.Cautions);
        Assert.Null(result.TargetCustomer);
        Assert.Null(result.SellingPoints);
    }

    [Fact]
    public void Parse_UnknownSourceDocument_DropsField()
    {
        // Model cited a document that was never supplied — treat as hallucinated provenance
        var json = """
            {
              "schemaVersion": 1,
              "productName": { "value": "Phantom", "sourceDocument": "ghost.pdf" },
              "sourceDocuments": ["spec.pdf"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json, [new("spec.pdf", "x")]);

        Assert.NotNull(result);
        Assert.Null(result.ProductName);
    }

    [Fact]
    public void Parse_BlankSourceDocument_DropsField()
    {
        var json = """
            {
              "schemaVersion": 1,
              "productName": { "value": "NoSource", "sourceDocument": "   " }
            }
            """;

        var result = _sut.ParseProductInfoResult(json, TwoDocs);

        Assert.NotNull(result);
        Assert.Null(result.ProductName);
    }

    // ─── Schema normalization ────────────────────────────────────────────────

    [Fact]
    public void Parse_MissingSchemaVersion_DefaultsToOne()
    {
        var json = """{"productName":{"value":"X","sourceDocument":"spec.pdf"}}""";

        var result = _sut.ParseProductInfoResult(json, [new("spec.pdf", "x")]);

        Assert.NotNull(result);
        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void Parse_MissingSourceDocuments_BackfilledFromInput()
    {
        var json = """{"schemaVersion":1}""";

        var result = _sut.ParseProductInfoResult(json, TwoDocs);

        Assert.NotNull(result);
        Assert.NotNull(result.SourceDocuments);
        Assert.Equal(["spec.pdf", "marketing.md"], result.SourceDocuments);
    }

    // ─── Markdown fence tolerance ────────────────────────────────────────────

    [Fact]
    public void Parse_JsonInMarkdownFence_StripsFence()
    {
        var json = "```json\n" + """{"schemaVersion":1}""" + "\n```";

        var result = _sut.ParseProductInfoResult(json);

        Assert.NotNull(result);
        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void Parse_JsonInUnlabelledFence_StripsFence()
    {
        var json = "```\n" + """{"schemaVersion":1}""" + "\n```";

        var result = _sut.ParseProductInfoResult(json);

        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_PascalCaseKeys_ParsesCorrectly()
    {
        var json = """
            {
              "SchemaVersion": 1,
              "ProductName": { "Value": "Acme", "SourceDocument": "spec.pdf" }
            }
            """;

        var result = _sut.ParseProductInfoResult(json, [new("spec.pdf", "x")]);

        Assert.NotNull(result);
        Assert.Equal("Acme", result.ProductName!.Value);
    }

    // ─── Malformed input ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        Assert.Null(_sut.ParseProductInfoResult("not json"));
    }

    [Fact]
    public void Parse_JsonArray_ReturnsNull()
    {
        Assert.Null(_sut.ParseProductInfoResult("[1,2,3]"));
    }

    [Fact]
    public void Parse_TruncatedJson_ReturnsNull()
    {
        Assert.Null(_sut.ParseProductInfoResult("""{"schemaVersion":1,"productName":"""));
    }
}
