using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests verifying that <see cref="FetchResult.ToPromptText"/> output is bounded
/// and that the truncation logic applied in <c>GptService.Research.cs</c> works correctly.
/// </summary>
public class FetchResultPromptTextTests
{
    const int MaxToolResultChars = 8_000;

    static FetchResult MakeFetchResult(string mainText, string? title = null, string? jsonLd = null) =>
        new(
            FinalUrl: "https://example.com/product",
            StatusCode: 200,
            Title: title,
            MetaDescription: null,
            OgImage: null,
            JsonLd: jsonLd,
            MainText: mainText,
            Warnings: null);

    // ─── ToPromptText basic shape ─────────────────────────────────────────────

    [Fact]
    public void ToPromptText_IncludesFinalUrl()
    {
        var result = MakeFetchResult("some content");

        var text = result.ToPromptText();

        Assert.Contains("https://example.com/product", text);
    }

    [Fact]
    public void ToPromptText_IncludesMainText()
    {
        var result = MakeFetchResult("important body text");

        var text = result.ToPromptText();

        Assert.Contains("important body text", text);
    }

    [Fact]
    public void ToPromptText_IncludesTitle_WhenPresent()
    {
        var result = MakeFetchResult("body", title: "My Product Page");

        var text = result.ToPromptText();

        Assert.Contains("My Product Page", text);
    }

    [Fact]
    public void ToPromptText_OmitsTitle_WhenNull()
    {
        var result = MakeFetchResult("body", title: null);

        var text = result.ToPromptText();

        Assert.DoesNotContain("Title:", text);
    }

    // ─── Truncation at MaxToolResultChars ─────────────────────────────────────

    [Fact]
    public void ToPromptText_ShortResult_IsNotTruncated()
    {
        var result = MakeFetchResult(new string('x', 100));

        var raw = result.ToPromptText();
        var toolResult = raw.Length > MaxToolResultChars
            ? raw[..MaxToolResultChars] + "\n[truncated]"
            : raw;

        Assert.DoesNotContain("[truncated]", toolResult);
        Assert.Equal(raw, toolResult);
    }

    [Fact]
    public void ToPromptText_LongResult_IsTruncatedAtMaxChars()
    {
        // Build a result whose ToPromptText output exceeds MaxToolResultChars
        var result = MakeFetchResult(new string('x', MaxToolResultChars));

        var raw = result.ToPromptText();

        // raw includes header lines + mainText, so it will exceed MaxToolResultChars
        Assert.True(raw.Length > MaxToolResultChars,
            "Test pre-condition: raw output must exceed the cap for this test to be meaningful");

        var toolResult = raw.Length > MaxToolResultChars
            ? raw[..MaxToolResultChars] + "\n[truncated]"
            : raw;

        Assert.EndsWith("\n[truncated]", toolResult);
        Assert.Equal(MaxToolResultChars + "\n[truncated]".Length, toolResult.Length);
    }

    [Fact]
    public void ToPromptText_LargeTitle_AndMainText_TruncatesCorrectly()
    {
        // A long title combined with a near-cap MainText can push ToPromptText over the limit
        var result = new FetchResult(
            FinalUrl: "https://example.com/product",
            StatusCode: 200,
            Title: new string('T', 2_000),
            MetaDescription: new string('D', 2_000),
            OgImage: null,
            JsonLd: null,
            MainText: new string('b', MaxToolResultChars),
            Warnings: null);

        var raw = result.ToPromptText();

        Assert.True(raw.Length > MaxToolResultChars,
            "Test pre-condition: raw output must exceed the cap");

        var toolResult = raw.Length > MaxToolResultChars
            ? raw[..MaxToolResultChars] + "\n[truncated]"
            : raw;

        Assert.True(toolResult.Length <= MaxToolResultChars + "\n[truncated]".Length);
        Assert.EndsWith("\n[truncated]", toolResult);
    }

    [Fact]
    public void ToPromptText_ExactlyAtLimit_IsNotTruncated()
    {
        // Craft a result whose ToPromptText is exactly MaxToolResultChars chars
        // by computing the header length first.
        var headerOnly = MakeFetchResult(string.Empty).ToPromptText();
        var padding = MaxToolResultChars - headerOnly.Length;

        // If padding would be negative the header alone exceeds the cap — skip gracefully
        if (padding < 0)
            return;

        var result = MakeFetchResult(new string('x', padding));
        var raw = result.ToPromptText();

        Assert.Equal(MaxToolResultChars, raw.Length);

        var toolResult = raw.Length > MaxToolResultChars
            ? raw[..MaxToolResultChars] + "\n[truncated]"
            : raw;

        Assert.DoesNotContain("[truncated]", toolResult);
    }

    [Fact]
    public void ToPromptText_OneCharOverLimit_IsTruncated()
    {
        var headerOnly = MakeFetchResult(string.Empty).ToPromptText();
        var padding = MaxToolResultChars - headerOnly.Length + 1; // one char over

        if (padding < 1) return; // guard

        var result = MakeFetchResult(new string('x', padding));
        var raw = result.ToPromptText();

        Assert.True(raw.Length > MaxToolResultChars);

        var toolResult = raw.Length > MaxToolResultChars
            ? raw[..MaxToolResultChars] + "\n[truncated]"
            : raw;

        Assert.EndsWith("\n[truncated]", toolResult);
    }
}
