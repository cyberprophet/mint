#pragma warning disable OPENAI001

using OpenAI.Images;

using ShareInvest.Agency.Google;
using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

using System.Reflection;
using System.Text.Json;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Exhaustive coverage tests for model types, exception types, GeminiService,
/// and ImageQuality that were previously at 0–75% line coverage.
/// </summary>
public class ModelCoverageTests
{
    static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // ─── GeminiService ────────────────────────────────────────────────────────

    [Fact]
    public void GeminiService_Constructor_SetsApiKey()
    {
        // GeminiService is a thin wrapper — construction must not throw.
        var service = new GeminiService("fake-key");
        Assert.NotNull(service);
    }

    [Fact]
    public void GeminiService_Client_ReturnsNonNull()
    {
        var service = new GeminiService("fake-key");
        // Client property creates a new Google.GenAI.Client on each access.
        // We verify it doesn't throw and returns a non-null reference.
        Assert.NotNull(service.Client);
    }

    [Fact]
    public void GeminiService_ClientProperty_CanBeAccessedMultipleTimes()
    {
        var service = new GeminiService("fake-key");
        _ = service.Client;
        _ = service.Client; // second access must not throw
    }

    // ─── ImageQuality ──────────────────────────────────────────────────────────

    [Fact]
    public void ImageQuality_Medium_ReturnsExpectedValue()
    {
        var value = ImageQuality.Medium;
        Assert.Equal(GeneratedImageQuality.MediumQuality, value);
    }

    [Fact]
    public void ImageQuality_High_ReturnsExpectedValue()
    {
        var value = ImageQuality.High;
        Assert.Equal(GeneratedImageQuality.HighQuality, value);
    }

    [Fact]
    public void ImageQuality_Medium_DiffersFromHigh()
    {
        Assert.NotEqual(ImageQuality.Medium, ImageQuality.High);
    }

    // ─── ImageGenerationRequest ───────────────────────────────────────────────

    [Fact]
    public void ImageGenerationRequest_Constructor_SetsAllProperties()
    {
        var req = new ImageGenerationRequest(
            UserId: "user-1",
            Path: "images/output.png",
            SceneId: "scene-42",
            Prompt: "A serene mountain landscape",
            AspectRatio: "16:9",
            Quality: GeneratedImageQuality.HighQuality,
            SessionId: "sess-99");

        Assert.Equal("user-1", req.UserId);
        Assert.Equal("images/output.png", req.Path);
        Assert.Equal("scene-42", req.SceneId);
        Assert.Equal("A serene mountain landscape", req.Prompt);
        Assert.Equal("16:9", req.AspectRatio);
        Assert.Equal(GeneratedImageQuality.HighQuality, req.Quality);
        Assert.Equal("sess-99", req.SessionId);
    }

    [Fact]
    public void ImageGenerationRequest_DefaultQuality_IsNull()
    {
        var req = new ImageGenerationRequest("u", "p", "s", "prompt", "1:1");
        Assert.Null(req.Quality);
    }

    [Fact]
    public void ImageGenerationRequest_DefaultSessionId_IsNull()
    {
        var req = new ImageGenerationRequest("u", "p", "s", "prompt", "1:1");
        Assert.Null(req.SessionId);
    }

    [Fact]
    public void ImageGenerationRequest_RecordEquality_EqualWhenSameValues()
    {
        var a = new ImageGenerationRequest("u", "p", "s", "prompt", "1:1", null, "sess");
        var b = new ImageGenerationRequest("u", "p", "s", "prompt", "1:1", null, "sess");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ImageGenerationRequest_RecordEquality_NotEqualWhenDifferentPrompt()
    {
        var a = new ImageGenerationRequest("u", "p", "s", "prompt A", "1:1");
        var b = new ImageGenerationRequest("u", "p", "s", "prompt B", "1:1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ImageGenerationRequest_WithExpression_CreatesNewRecord()
    {
        var original = new ImageGenerationRequest("u", "p", "s", "original prompt", "1:1");
        var modified = original with { Prompt = "new prompt" };

        Assert.Equal("original prompt", original.Prompt);
        Assert.Equal("new prompt", modified.Prompt);
    }

    // ─── ImageGenerationModerationException ───────────────────────────────────

    [Fact]
    public void ImageGenerationModerationException_MessageOnly_SetsMessage()
    {
        var ex = new ImageGenerationModerationException("content policy violation");
        Assert.Equal("content policy violation", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void ImageGenerationModerationException_WithInner_SetsMessageAndInner()
    {
        var inner = new Exception("root cause");
        var ex = new ImageGenerationModerationException("blocked", inner);

        Assert.Equal("blocked", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ImageGenerationModerationException_IsException()
    {
        var ex = new ImageGenerationModerationException("test");
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void ImageGenerationModerationException_EmptyMessage_IsAllowed()
    {
        // Constructor does not validate — empty message is legal.
        var ex = new ImageGenerationModerationException(string.Empty);
        Assert.Equal(string.Empty, ex.Message);
    }

    // ─── DesignHtmlContext ─────────────────────────────────────────────────────

    static BlueprintResult MakeBlueprintResult() => new(
        PageDesignSystem: new PageDesignSystem(
            Mood: "Premium",
            BrandColors: ["#ffffff", "#000000"],
            BackgroundApproach: "white studio",
            TypographyScale: "display-xl, body-md"),
        VisualBlocks: [],
        Assumptions: null);

    static StoryboardResult MakeStoryboardResult() => new(
        Sections: [],
        CtaText: "Buy Now");

    [Fact]
    public void DesignHtmlContext_Constructor_SetsAllFields()
    {
        var blueprint = MakeBlueprintResult();
        var storyboard = MakeStoryboardResult();

        var ctx = new DesignHtmlContext(blueprint, storyboard, Brief: null, Feedback: null);

        Assert.Same(blueprint, ctx.Blueprint);
        Assert.Same(storyboard, ctx.Storyboard);
        Assert.Null(ctx.Brief);
        Assert.Null(ctx.Feedback);
    }

    [Fact]
    public void DesignHtmlContext_WithBriefAndFeedback_SetsCorrectly()
    {
        var blueprint = MakeBlueprintResult();
        var storyboard = MakeStoryboardResult();
        var brief = new { product = "Widget" };

        var ctx = new DesignHtmlContext(blueprint, storyboard, Brief: brief, Feedback: "Fix the header");

        Assert.NotNull(ctx.Brief);
        Assert.Equal("Fix the header", ctx.Feedback);
    }

    [Fact]
    public void DesignHtmlContext_RecordEquality_EqualWhenSameValues()
    {
        var blueprint = MakeBlueprintResult();
        var storyboard = MakeStoryboardResult();

        var a = new DesignHtmlContext(blueprint, storyboard, null, null);
        var b = new DesignHtmlContext(blueprint, storyboard, null, null);

        Assert.Equal(a, b);
    }

    // ─── DesignHtmlResult ─────────────────────────────────────────────────────

    [Fact]
    public void DesignHtmlResult_Constructor_SetsAllFields()
    {
        var result = new DesignHtmlResult("<html></html>", TokensUsed: 1500, Attempts: 2);

        Assert.Equal("<html></html>", result.Html);
        Assert.Equal(1500, result.TokensUsed);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public void DesignHtmlResult_EmptyHtml_IsAllowed()
    {
        var result = new DesignHtmlResult(Html: string.Empty, TokensUsed: 0, Attempts: 1);
        Assert.Equal(string.Empty, result.Html);
    }

    [Fact]
    public void DesignHtmlResult_RecordEquality_EqualWhenSameValues()
    {
        var a = new DesignHtmlResult("<html/>", 100, 1);
        var b = new DesignHtmlResult("<html/>", 100, 1);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DesignHtmlResult_RecordEquality_NotEqualWhenDifferentTokens()
    {
        var a = new DesignHtmlResult("<html/>", 100, 1);
        var b = new DesignHtmlResult("<html/>", 200, 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DesignHtmlResult_WithExpression_UpdatesField()
    {
        var original = new DesignHtmlResult("<div/>", 50, 1);
        var updated = original with { Attempts = 3 };

        Assert.Equal(1, original.Attempts);
        Assert.Equal(3, updated.Attempts);
    }

    // ─── ProductData ──────────────────────────────────────────────────────────

    [Fact]
    public void ProductData_Constructor_SetsAllFields()
    {
        var features = new[] { "Fast", "Reliable" };
        var pd = new ProductData(
            SourceUrl: "https://example.com/product",
            Title: "Widget Pro",
            Description: "Best widget ever",
            Price: "$29.99",
            Brand: "WidgetCo",
            Features: features,
            OgImage: "https://example.com/img.jpg",
            SchemaType: "Product");

        Assert.Equal("https://example.com/product", pd.SourceUrl);
        Assert.Equal("Widget Pro", pd.Title);
        Assert.Equal("Best widget ever", pd.Description);
        Assert.Equal("$29.99", pd.Price);
        Assert.Equal("WidgetCo", pd.Brand);
        Assert.Equal(new[] { "Fast", "Reliable" }, pd.Features);
        Assert.Equal("https://example.com/img.jpg", pd.OgImage);
        Assert.Equal("Product", pd.SchemaType);
    }

    [Fact]
    public void ProductData_NullableFields_CanBeNull()
    {
        var pd = new ProductData(
            SourceUrl: "https://example.com",
            Title: null,
            Description: null,
            Price: null,
            Brand: null,
            Features: null,
            OgImage: null,
            SchemaType: null);

        Assert.Null(pd.Title);
        Assert.Null(pd.Price);
        Assert.Null(pd.Features);
    }

    [Fact]
    public void ProductData_DeserializesFromJson()
    {
        const string json = """
            {
              "sourceUrl": "https://shop.example.com/item",
              "title": "Premium Widget",
              "description": "High quality",
              "price": "$49.00",
              "brand": "BrandX",
              "features": ["Durable", "Compact"],
              "ogImage": "https://cdn.example.com/img.png",
              "schemaType": "Product"
            }
            """;

        var pd = JsonSerializer.Deserialize<ProductData>(json, CaseInsensitive);

        Assert.NotNull(pd);
        Assert.Equal("https://shop.example.com/item", pd.SourceUrl);
        Assert.Equal("Premium Widget", pd.Title);
        Assert.Contains("Durable", pd.Features ?? []);
    }

    [Fact]
    public void ProductData_RecordEquality_EqualWhenSameValues()
    {
        var a = new ProductData("https://a.com", "Title", null, null, null, null, null, null);
        var b = new ProductData("https://a.com", "Title", null, null, null, null, null, null);
        Assert.Equal(a, b);
    }

    // ─── CompetitorInsight ────────────────────────────────────────────────────

    [Fact]
    public void CompetitorInsight_Constructor_SetsAllFields()
    {
        var diffs = new[] { "Price", "Quality" };
        var ci = new CompetitorInsight(
            Name: "CompetitorA",
            Url: "https://competitor-a.com",
            Positioning: "Budget-friendly",
            PriceRange: "$10–$30",
            VisualStyle: "clinical white",
            Differentiators: diffs);

        Assert.Equal("CompetitorA", ci.Name);
        Assert.Equal("https://competitor-a.com", ci.Url);
        Assert.Equal("Budget-friendly", ci.Positioning);
        Assert.Equal("$10–$30", ci.PriceRange);
        Assert.Equal("clinical white", ci.VisualStyle);
        Assert.Equal(new[] { "Price", "Quality" }, ci.Differentiators);
    }

    [Fact]
    public void CompetitorInsight_NullableFields_CanBeNull()
    {
        var ci = new CompetitorInsight(
            Name: "CompB",
            Url: "https://compb.com",
            Positioning: "Unknown",
            PriceRange: null,
            VisualStyle: null,
            Differentiators: null);

        Assert.Null(ci.PriceRange);
        Assert.Null(ci.VisualStyle);
        Assert.Null(ci.Differentiators);
    }

    [Fact]
    public void CompetitorInsight_DeserializesFromJson()
    {
        const string json = """
            {
              "name": "BrandY",
              "url": "https://brandy.com",
              "positioning": "Premium market leader",
              "priceRange": "$100–$200",
              "visualStyle": "premium dark",
              "differentiators": ["Organic", "Cruelty-free"]
            }
            """;

        var ci = JsonSerializer.Deserialize<CompetitorInsight>(json, CaseInsensitive);

        Assert.NotNull(ci);
        Assert.Equal("BrandY", ci.Name);
        Assert.Equal("premium dark", ci.VisualStyle);
        Assert.Contains("Organic", ci.Differentiators ?? []);
    }

    [Fact]
    public void CompetitorInsight_RecordEquality_EqualWhenSameValues()
    {
        var a = new CompetitorInsight("C", "https://c.com", "Budget", null, null, null);
        var b = new CompetitorInsight("C", "https://c.com", "Budget", null, null, null);
        Assert.Equal(a, b);
    }

    // ─── ResearchResult with ProductData + CompetitorInsights ─────────────────

    [Fact]
    public void ResearchResult_WithProductData_DeserializesCorrectly()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "productData": [
                {
                  "sourceUrl": "https://product.example.com",
                  "title": "Super Widget",
                  "description": "The best",
                  "price": "$19.99",
                  "brand": "SuperBrand",
                  "features": ["Fast", "Reliable"],
                  "ogImage": "https://cdn.example.com/img.png",
                  "schemaType": "Product"
                }
              ],
              "competitorInsights": [],
              "basis": "research"
            }
            """;

        var result = JsonSerializer.Deserialize<ResearchResult>(json, CaseInsensitive);

        Assert.NotNull(result);
        Assert.Single(result.ProductData);
        Assert.Equal("Super Widget", result.ProductData[0].Title);
        Assert.Equal("SuperBrand", result.ProductData[0].Brand);
    }

    [Fact]
    public void ResearchResult_WithCompetitorInsights_DeserializesCorrectly()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "productData": [],
              "competitorInsights": [
                {
                  "name": "CompX",
                  "url": "https://compx.com",
                  "positioning": "Value leader",
                  "priceRange": "$5–$15",
                  "visualStyle": "minimal",
                  "differentiators": ["Affordable"]
                }
              ],
              "basis": "research"
            }
            """;

        var result = JsonSerializer.Deserialize<ResearchResult>(json, CaseInsensitive);

        Assert.NotNull(result);
        Assert.Single(result.CompetitorInsights);
        Assert.Equal("CompX", result.CompetitorInsights[0].Name);
        Assert.Equal("Value leader", result.CompetitorInsights[0].Positioning);
    }

    [Fact]
    public void ResearchResult_AllOptionalFieldsNull_IsValid()
    {
        var result = new ResearchResult(
            SchemaVersion: 2,
            ProductData: [],
            CompetitorInsights: [],
            MarketContext: null,
            SynthesizedInsights: null,
            Category: null,
            CoreValue: null,
            KeySellingPoints: null,
            RecommendedAngle: null,
            Basis: "research");

        Assert.Null(result.MarketContext);
        Assert.Null(result.Category);
        Assert.Null(result.KeySellingPoints);
        Assert.Equal("research", result.Basis);
    }

    [Fact]
    public void ResearchResult_WithExpression_UpdatesFields()
    {
        var original = new ResearchResult(2, [], [], null, null, null, null, null, null, "research");
        var updated = original with { SchemaVersion = 3, Category = "Electronics" };

        Assert.Equal(2, original.SchemaVersion);
        Assert.Equal(3, updated.SchemaVersion);
        Assert.Equal("Electronics", updated.Category);
    }

    // ─── StoryboardContext ────────────────────────────────────────────────────

    [Fact]
    public void StoryboardContext_Constructor_SetsAllFields()
    {
        var ctx = new StoryboardContext(
            Brief: "{\"productName\":\"Widget\"}",
            MarketContext: "{\"category\":\"Electronics\"}",
            VisualDna: "[{\"mood\":\"premium\"}]",
            TargetLanguage: "en",
            ForbiddenCliches: ["amazing", "world-class"],
            ProductType: "laptop",
            Feedback: null);

        Assert.Equal("{\"productName\":\"Widget\"}", ctx.Brief);
        Assert.Equal("en", ctx.TargetLanguage);
        Assert.Equal(new[] { "amazing", "world-class" }, ctx.ForbiddenCliches);
        Assert.Equal("laptop", ctx.ProductType);
        Assert.Null(ctx.Feedback);
    }

    [Fact]
    public void StoryboardContext_NullVisualDna_IsAllowed()
    {
        var ctx = new StoryboardContext("{}", "{}", null, "ko", null, null, null);
        Assert.Null(ctx.VisualDna);
    }

    [Fact]
    public void StoryboardContext_NullForbiddenCliches_IsAllowed()
    {
        var ctx = new StoryboardContext("{}", "{}", "[]", "en", null, "tumbler", null);
        Assert.Null(ctx.ForbiddenCliches);
    }

    [Fact]
    public void StoryboardContext_WithFeedback_SetsCorrectly()
    {
        var ctx = new StoryboardContext("{}", "{}", null, "en", null, null, "Fix missing spec table");
        Assert.Equal("Fix missing spec table", ctx.Feedback);
    }

    [Fact]
    public void StoryboardContext_RecordEquality_EqualWhenSameValues()
    {
        var a = new StoryboardContext("{}", "{}", null, "en", null, null, null);
        var b = new StoryboardContext("{}", "{}", null, "en", null, null, null);
        Assert.Equal(a, b);
    }

    [Fact]
    public void StoryboardContext_WithExpression_UpdatesField()
    {
        var original = new StoryboardContext("{}", "{}", null, "en", null, null, null);
        var updated = original with { TargetLanguage = "ko", Feedback = "retry" };

        Assert.Equal("en", original.TargetLanguage);
        Assert.Equal("ko", updated.TargetLanguage);
        Assert.Equal("retry", updated.Feedback);
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("zh")]
    public void StoryboardContext_SupportedLanguageCodes_AreAccepted(string lang)
    {
        var ctx = new StoryboardContext("{}", "{}", null, lang, null, null, null);
        Assert.Equal(lang, ctx.TargetLanguage);
    }

    // ─── GptService.MapSize (via ImageGenerationRequest with different aspect ratios) ─

    /// <summary>
    /// Exercises the MapSize switch arms by reading the private static method via reflection.
    /// This covers lines in GptService.Image.cs that are otherwise only reachable when hitting
    /// the real OpenAI API.
    /// </summary>
    static GeneratedImageSize InvokeMapSize(string aspectRatio)
    {
        var method = typeof(GptService).GetMethod("MapSize",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(GptService), "MapSize");

        return (GeneratedImageSize)method.Invoke(null, [aspectRatio])!;
    }

    [Fact]
    public void MapSize_Portrait_Returns1024x1536()
    {
        var size = InvokeMapSize("9:16");
        Assert.Equal(GeneratedImageSize.W1024xH1536, size);
    }

    [Fact]
    public void MapSize_Landscape_Returns1536x1024()
    {
        var size = InvokeMapSize("16:9");
        Assert.Equal(GeneratedImageSize.W1536xH1024, size);
    }

    [Fact]
    public void MapSize_Square_Returns1024x1024()
    {
        var size = InvokeMapSize("1:1");
        Assert.Equal(GeneratedImageSize.W1024xH1024, size);
    }

    [Theory]
    [InlineData("4:3")]
    [InlineData("3:4")]
    [InlineData("unknown")]
    [InlineData("")]
    public void MapSize_UnknownRatio_DefaultsToSquare(string ratio)
    {
        var size = InvokeMapSize(ratio);
        Assert.Equal(GeneratedImageSize.W1024xH1024, size);
    }

    // ─── FetchResult.ToPromptText with JsonLd ────────────────────────────────

    [Fact]
    public void FetchResult_ToPromptText_WithJsonLd_DoesNotIncludeJsonLdInOutput()
    {
        // JsonLd is stored but ToPromptText does not include it to avoid bloating prompts.
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: "My Page",
            MetaDescription: null,
            OgImage: null,
            JsonLd: "{\"@type\":\"Product\"}",
            MainText: "Content here",
            Warnings: null);

        var text = result.ToPromptText();

        // The method currently does NOT emit JsonLd — verify it's not accidentally included.
        Assert.DoesNotContain("@type", text);
        Assert.Contains("My Page", text);
        Assert.Contains("Content here", text);
    }

    [Fact]
    public void FetchResult_ToPromptText_WithWarnings_DoesNotIncludeWarningsInOutput()
    {
        // Warnings are metadata — ToPromptText is for GPT context, not debug.
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: null,
            MetaDescription: null,
            OgImage: null,
            JsonLd: null,
            MainText: "text",
            Warnings: ["Content truncated to 8,000 characters"]);

        var text = result.ToPromptText();
        Assert.DoesNotContain("truncated", text);
        Assert.Contains("text", text);
    }

    [Fact]
    public void FetchResult_RecordEquality_EqualWhenSameValues()
    {
        var a = new FetchResult("https://a.com", 200, null, null, null, null, "text", null);
        var b = new FetchResult("https://a.com", 200, null, null, null, null, "text", null);
        Assert.Equal(a, b);
    }
}
