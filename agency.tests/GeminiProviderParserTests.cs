using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.Google;
using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for the three private-until-now JSON parser helpers on
/// <see cref="GeminiProvider"/>: <c>TryParseVisualDna</c>, <c>TryParseProductInfo</c>,
/// and <c>TryParseReferenceLinkAnalysis</c>.
///
/// <para>Closes #84 — these helpers previously had zero coverage. They convert the
/// raw text returned by Gemini (which may arrive wrapped in a markdown fence, prefixed
/// with commentary, or otherwise noisy) into strongly-typed Agency DTOs. An untested
/// parser is a classic silent-drift risk when the upstream model's output format
/// shifts.</para>
///
/// <para>All JSON samples below follow the <b>output schemas</b> documented on the
/// corresponding DTOs (<see cref="VisualDnaResult"/>, <see cref="ProductInfoResult"/>,
/// <see cref="ReferenceLinkAnalysis"/>). No real prompt content is embedded — these
/// fixtures model the response shape only, consistent with ADR-013.</para>
/// </summary>
public class GeminiProviderParserTests
{
    // Gemini Client never dispatches in the constructor, so a placeholder key is safe
    // for unit tests that only exercise the parser helpers.
    readonly GeminiProvider _sut = new(NullLogger<GeminiProvider>.Instance, "test-key");

    // ─── TryParseVisualDna ────────────────────────────────────────────────────

    [Fact]
    public void TryParseVisualDna_WellFormedJson_ReturnsPopulatedResult()
    {
        var raw = """
            {
              "dominantColors": ["#FFFFFF", "#111111"],
              "mood": "premium",
              "materials": ["glass", "matte"],
              "style": "luxury-minimal",
              "backgroundType": "white-studio",
              "rawDescription": "A high-end product photographed on a clean white studio backdrop."
            }
            """;

        var result = _sut.TryParseVisualDna(raw);

        Assert.NotNull(result);
        Assert.Equal(2, result.DominantColors.Length);
        Assert.Equal("premium", result.Mood);
        Assert.Equal("luxury-minimal", result.Style);
        Assert.Equal("white-studio", result.BackgroundType);
        Assert.Contains("glass", result.Materials);
    }

    [Fact]
    public void TryParseVisualDna_NormalizesUnknownLabelsToUnknown()
    {
        var raw = """
            {
              "dominantColors": ["#ABC"],
              "mood": "holographic-dreamy",
              "materials": [],
              "style": "alien-tech",
              "backgroundType": "zero-gravity",
              "rawDescription": "Unrecognised labels should be normalised."
            }
            """;

        var result = _sut.TryParseVisualDna(raw);

        Assert.NotNull(result);
        // Normalize() runs inside the parser, so out-of-vocabulary labels become "unknown".
        Assert.Equal("unknown", result.Mood);
        Assert.Equal("unknown", result.Style);
        Assert.Equal("unknown", result.BackgroundType);
    }

    [Fact]
    public void TryParseVisualDna_MarkdownFence_Unwraps()
    {
        var raw = "```json\n"
            + """{"dominantColors":["#000"],"mood":"minimal","materials":[],"style":"minimal","backgroundType":"solid","rawDescription":"x"}"""
            + "\n```";

        var result = _sut.TryParseVisualDna(raw);

        Assert.NotNull(result);
        Assert.Equal("minimal", result.Mood);
    }

    [Fact]
    public void TryParseVisualDna_PreambleBeforeJson_ExtractsJsonBlock()
    {
        var raw = "Sure, here is the analysis:\n\n"
            + """{"dominantColors":["#FFF"],"mood":"clinical","materials":[],"style":"clinical","backgroundType":"solid","rawDescription":"x"}"""
            + "\n\nLet me know if you need more.";

        var result = _sut.TryParseVisualDna(raw);

        Assert.NotNull(result);
        Assert.Equal("clinical", result.Mood);
    }

    [Fact]
    public void TryParseVisualDna_ExtraUnknownFields_AreIgnored()
    {
        var raw = """
            {
              "dominantColors": ["#FFF"],
              "mood": "minimal",
              "materials": [],
              "style": "minimal",
              "backgroundType": "solid",
              "rawDescription": "x",
              "futureField": "forward-compatible payload",
              "nested": { "a": 1 }
            }
            """;

        var result = _sut.TryParseVisualDna(raw);

        Assert.NotNull(result);
        Assert.Equal("minimal", result.Mood);
    }

    [Fact]
    public void TryParseVisualDna_MalformedJson_ReturnsNull()
    {
        var raw = """{"dominantColors":["#FFF"],"mood":"minimal",""";

        var result = _sut.TryParseVisualDna(raw);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseVisualDna_NoJsonObject_ReturnsNull()
    {
        Assert.Null(_sut.TryParseVisualDna("the model refused to produce structured output"));
    }

    [Fact]
    public void TryParseVisualDna_EmptyString_ReturnsNull()
    {
        Assert.Null(_sut.TryParseVisualDna(string.Empty));
    }

    [Fact]
    public void TryParseVisualDna_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(_sut.TryParseVisualDna("   \n\t "));
    }

    // ─── TryParseProductInfo ──────────────────────────────────────────────────

    static readonly IReadOnlyList<ProductInfoDocument> TwoDocs =
    [
        new("spec.pdf", "ignored body"),
        new("marketing.md", "ignored body")
    ];

    [Fact]
    public void TryParseProductInfo_WellFormedJson_ReturnsPopulatedResult()
    {
        var raw = """
            {
              "schemaVersion": 1,
              "productName":   { "value": "Acme Widget",             "sourceDocument": "spec.pdf" },
              "oneLiner":      { "value": "The quiet revolution.",    "sourceDocument": "marketing.md" },
              "keyFeatures":   { "value": ["fast","quiet","durable"], "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf", "marketing.md"]
            }
            """;

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

        Assert.NotNull(result);
        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal("Acme Widget", result.ProductName!.Value);
        Assert.Equal("marketing.md", result.OneLiner!.SourceDocument);
        Assert.Equal(3, result.KeyFeatures!.Value.Length);
    }

    [Fact]
    public void TryParseProductInfo_FiltersUnknownSourceDocuments()
    {
        // "ghost.pdf" is not among the input documents — it must be filtered out of
        // the returned SourceDocuments array (no hallucinated provenance survives).
        var raw = """
            {
              "schemaVersion": 1,
              "productName": { "value": "X", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf", "ghost.pdf"]
            }
            """;

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

        Assert.NotNull(result);
        Assert.NotNull(result.SourceDocuments);
        Assert.Single(result.SourceDocuments!);
        Assert.Equal("spec.pdf", result.SourceDocuments![0]);
    }

    [Fact]
    public void TryParseProductInfo_SourceDocumentsMatchedCaseInsensitively()
    {
        // OrdinalIgnoreCase — "Spec.PDF" should still match the input id "spec.pdf".
        var raw = """
            {
              "schemaVersion": 1,
              "sourceDocuments": ["Spec.PDF"]
            }
            """;

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

        Assert.NotNull(result);
        Assert.NotNull(result.SourceDocuments);
        Assert.Single(result.SourceDocuments!);
    }

    [Fact]
    public void TryParseProductInfo_MissingOptionalFields_LeavesThemNull()
    {
        var raw = """
            {
              "schemaVersion": 1,
              "productName": { "value": "OnlyName", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf"]
            }
            """;

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

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
    public void TryParseProductInfo_ExtraUnknownFields_AreIgnored()
    {
        var raw = """
            {
              "schemaVersion": 1,
              "productName": { "value": "X", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf"],
              "futureField": 42,
              "reservedFlags": { "experimental": true }
            }
            """;

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

        Assert.NotNull(result);
        Assert.Equal("X", result.ProductName!.Value);
    }

    [Fact]
    public void TryParseProductInfo_MarkdownFence_Unwraps()
    {
        var raw = "```json\n"
            + """{"schemaVersion":1,"sourceDocuments":["spec.pdf"]}"""
            + "\n```";

        var result = _sut.TryParseProductInfo(raw, TwoDocs);

        Assert.NotNull(result);
        Assert.Equal(1, result.SchemaVersion);
    }

    [Fact]
    public void TryParseProductInfo_MalformedJson_ReturnsNull()
    {
        Assert.Null(_sut.TryParseProductInfo("""{"schemaVersion":1,"productName":""", TwoDocs));
    }

    [Fact]
    public void TryParseProductInfo_NoJsonObject_ReturnsNull()
    {
        Assert.Null(_sut.TryParseProductInfo("model returned no structured output", TwoDocs));
    }

    [Fact]
    public void TryParseProductInfo_EmptyString_ReturnsNull()
    {
        Assert.Null(_sut.TryParseProductInfo(string.Empty, TwoDocs));
    }

    [Fact]
    public void TryParseProductInfo_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(_sut.TryParseProductInfo("   \n\t ", TwoDocs));
    }

    // ─── TryParseReferenceLinkAnalysis ────────────────────────────────────────

    [Fact]
    public void TryParseReferenceLinkAnalysis_WellFormedJson_ReturnsPopulatedResult()
    {
        var raw = """
            {
              "layoutPattern": "hero-problem-solution-cta",
              "copyTone": "warm-editorial",
              "colorPalette": ["#F7F3EE", "#1A1A1A", "#C2A878"],
              "typographyStyle": "serif-heavy-editorial",
              "messagingAngles": ["heritage craft", "everyday ritual", "honest price"],
              "rawSummary": "A slow, editorial landing page that frames a commodity product as an everyday ritual."
            }
            """;

        var result = _sut.TryParseReferenceLinkAnalysis(raw);

        Assert.NotNull(result);
        Assert.Equal("hero-problem-solution-cta", result.LayoutPattern);
        Assert.Equal("warm-editorial", result.CopyTone);
        Assert.Equal(3, result.ColorPalette.Length);
        Assert.Equal("serif-heavy-editorial", result.TypographyStyle);
        Assert.Equal(3, result.MessagingAngles.Length);
        Assert.Contains("ritual", result.RawSummary);
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_MarkdownFence_Unwraps()
    {
        var raw = "```json\n"
            + """{"layoutPattern":"grid","copyTone":"minimal","colorPalette":["#000"],"typographyStyle":"sans-minimal","messagingAngles":["price"],"rawSummary":"x"}"""
            + "\n```";

        var result = _sut.TryParseReferenceLinkAnalysis(raw);

        Assert.NotNull(result);
        Assert.Equal("grid", result.LayoutPattern);
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_ExtraFields_AreIgnored()
    {
        var raw = """
            {
              "layoutPattern": "grid",
              "copyTone": "minimal",
              "colorPalette": [],
              "typographyStyle": "sans",
              "messagingAngles": [],
              "rawSummary": "x",
              "experimentalField": "ignored",
              "nested": { "debug": true }
            }
            """;

        var result = _sut.TryParseReferenceLinkAnalysis(raw);

        Assert.NotNull(result);
        Assert.Equal("grid", result.LayoutPattern);
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_MissingArrayFields_ReturnsNullArrays()
    {
        // System.Text.Json leaves omitted string[] members null — parser does not coerce.
        var raw = """
            {
              "layoutPattern": "hero",
              "copyTone": "minimal",
              "typographyStyle": "sans",
              "rawSummary": "x"
            }
            """;

        var result = _sut.TryParseReferenceLinkAnalysis(raw);

        Assert.NotNull(result);
        Assert.Equal("hero", result.LayoutPattern);
        Assert.Null(result.ColorPalette);
        Assert.Null(result.MessagingAngles);
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_MalformedJson_ReturnsNull()
    {
        Assert.Null(_sut.TryParseReferenceLinkAnalysis("""{"layoutPattern":"hero","copyTone":"""));
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_NoJsonObject_ReturnsNull()
    {
        Assert.Null(_sut.TryParseReferenceLinkAnalysis("no structured output here"));
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_EmptyString_ReturnsNull()
    {
        Assert.Null(_sut.TryParseReferenceLinkAnalysis(string.Empty));
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_WhitespaceOnly_ReturnsNull()
    {
        Assert.Null(_sut.TryParseReferenceLinkAnalysis("   \n\t "));
    }

    [Fact]
    public void TryParseReferenceLinkAnalysis_PreambleBeforeJson_ExtractsJsonBlock()
    {
        var raw = "Analysis complete.\n\n"
            + """{"layoutPattern":"hero","copyTone":"minimal","colorPalette":["#000"],"typographyStyle":"sans","messagingAngles":["price"],"rawSummary":"x"}"""
            + "\n\nDone.";

        var result = _sut.TryParseReferenceLinkAnalysis(raw);

        Assert.NotNull(result);
        Assert.Equal("hero", result.LayoutPattern);
    }
}
