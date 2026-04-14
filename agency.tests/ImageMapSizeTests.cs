using System.Reflection;

using OpenAI.Images;

using ShareInvest.Agency.OpenAI;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <c>GptService.MapSize(aspectRatio)</c> — the static helper that
/// converts aspect-ratio strings into <see cref="GeneratedImageSize"/> values.
///
/// Regression guard: an incorrect mapping here causes every batch image generation
/// request to use the wrong canvas size, which P5 cannot detect until the image is
/// delivered. Each branch of the switch expression is covered individually.
/// </summary>
public class ImageMapSizeTests
{
    // ─── Reflection handle ────────────────────────────────────────────────────

    static readonly MethodInfo MapSizeMethod =
        typeof(GptService).GetMethod("MapSize",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(GptService), "MapSize");

    static GeneratedImageSize MapSize(string aspectRatio) =>
        (GeneratedImageSize)MapSizeMethod.Invoke(null, [aspectRatio])!;

    // ─── Portrait "9:16" ─────────────────────────────────────────────────────

    [Fact]
    public void MapSize_Portrait_9x16_ReturnsW1024H1536()
    {
        var size = MapSize("9:16");

        Assert.Equal(GeneratedImageSize.W1024xH1536, size);
    }

    // ─── Landscape "16:9" ────────────────────────────────────────────────────

    [Fact]
    public void MapSize_Landscape_16x9_ReturnsW1536H1024()
    {
        var size = MapSize("16:9");

        Assert.Equal(GeneratedImageSize.W1536xH1024, size);
    }

    // ─── Square (default for all other inputs) ───────────────────────────────

    [Fact]
    public void MapSize_Square_1x1_ReturnsW1024H1024()
    {
        var size = MapSize("1:1");

        Assert.Equal(GeneratedImageSize.W1024xH1024, size);
    }

    [Theory]
    [InlineData("4:5")]
    [InlineData("3:4")]
    [InlineData("2:3")]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("portrait")]
    public void MapSize_UnrecognizedRatio_FallsBackToSquare(string ratio)
    {
        var size = MapSize(ratio);

        Assert.Equal(GeneratedImageSize.W1024xH1024, size);
    }

    [Fact]
    public void MapSize_Portrait_And_Landscape_AreDifferentSizes()
    {
        var portrait = MapSize("9:16");
        var landscape = MapSize("16:9");

        // Portrait and landscape must never resolve to the same canvas
        Assert.NotEqual(portrait, landscape);
    }

    [Fact]
    public void MapSize_Portrait_IsNotSquare()
    {
        Assert.NotEqual(GeneratedImageSize.W1024xH1024, MapSize("9:16"));
    }

    [Fact]
    public void MapSize_Landscape_IsNotSquare()
    {
        Assert.NotEqual(GeneratedImageSize.W1024xH1024, MapSize("16:9"));
    }
}

/// <summary>
/// Unit tests for <see cref="PromptSanitizer.EscapeIdentifierForPrompt"/>.
///
/// This method is distinct from <c>EscapeForPrompt</c>: it HTML-escapes structural
/// characters without wrapping in <c>&lt;user_input&gt;</c> tags, so that the model
/// can echo the id verbatim and downstream id-equality checks still succeed.
/// </summary>
public class EscapeIdentifierForPromptTests
{
    // ─── Normal identifiers ───────────────────────────────────────────────────

    [Fact]
    public void NormalId_PassesThroughUnchanged()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("doc-001");
        Assert.Equal("doc-001", result);
    }

    [Fact]
    public void IdWithAlphanumericsAndDash_PassesThroughUnchanged()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("product_spec_v2");
        Assert.Equal("product_spec_v2", result);
    }

    // ─── Null / empty ─────────────────────────────────────────────────────────

    [Fact]
    public void NullId_ReturnsEmptyString()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EmptyId_ReturnsEmptyString()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    // ─── HTML special characters ──────────────────────────────────────────────

    [Fact]
    public void IdWithAmpersand_IsEscaped()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("doc&spec");
        Assert.Equal("doc&amp;spec", result);
    }

    [Fact]
    public void IdWithLessThan_IsEscaped()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("doc<injection");
        Assert.Equal("doc&lt;injection", result);
    }

    [Fact]
    public void IdWithGreaterThan_IsEscaped()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("doc>end");
        Assert.Equal("doc&gt;end", result);
    }

    [Fact]
    public void IdWithDoubleQuote_IsEscaped()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("doc\"id");
        Assert.Equal("doc&quot;id", result);
    }

    [Fact]
    public void IdWithAllSpecialChars_AllEscaped()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("<doc&\"id\">");
        Assert.Equal("&lt;doc&amp;&quot;id&quot;&gt;", result);
    }

    // ─── Injection resistance ─────────────────────────────────────────────────

    /// <summary>
    /// A document id containing a tag-break attempt must not break the surrounding
    /// attribute context in the prompt markup.
    /// </summary>
    [Fact]
    public void IdWithTagBreakAttempt_AllSpecialCharsEscaped()
    {
        var maliciousId = "\"><script>alert(1)</script><\"";
        var result = PromptSanitizer.EscapeIdentifierForPrompt(maliciousId);

        Assert.DoesNotContain("<script>", result);
        Assert.DoesNotContain("\"<", result);
        Assert.Contains("&lt;", result);
        Assert.Contains("&gt;", result);
        Assert.Contains("&quot;", result);
    }

    /// <summary>
    /// Unlike EscapeForPrompt, EscapeIdentifierForPrompt must NOT wrap in user_input tags —
    /// wrapping would break the id round-trip check in ParseProductInfoResult.
    /// </summary>
    [Fact]
    public void EscapeIdentifier_DoesNotWrapInUserInputTags()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("my-doc-id");

        Assert.DoesNotContain("<user_input>", result);
        Assert.DoesNotContain("</user_input>", result);
    }

    /// <summary>
    /// Unicode characters that are not HTML-structural must pass through unchanged
    /// so that non-ASCII document ids round-trip correctly.
    /// </summary>
    [Fact]
    public void IdWithUnicode_PassesThroughUnchanged()
    {
        var result = PromptSanitizer.EscapeIdentifierForPrompt("문서-001");
        Assert.Equal("문서-001", result);
    }
}
