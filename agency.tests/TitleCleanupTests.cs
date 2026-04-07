using System.Text.RegularExpressions;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Tests for title cleanup logic (think-block removal, truncation).
/// Logic is duplicated here inline to avoid pulling in the OpenAI SDK at test time.
/// </summary>
public class TitleCleanupTests
{
    // Mirrors GptService.CleanTitleResponse (private static)
    static readonly Regex ThinkBlockRegex = new(@"<think>[\s\S]*?</think>\s*", RegexOptions.Compiled);

    static string? Clean(string raw)
    {
        var cleaned = ThinkBlockRegex.Replace(raw, string.Empty);

        var title = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (title is null)
            return null;

        if (title.Length > 50)
            title = string.Concat(title.AsSpan(0, 47), "...");

        return title;
    }

    [Fact]
    public void ThinkBlockRemoved_ReturnsTitle()
    {
        var raw = "<think>thinking...</think>\nMy Product Title";
        var result = Clean(raw);
        Assert.Equal("My Product Title", result);
    }

    [Fact]
    public void PlainTitle_ReturnedAsIs()
    {
        var result = Clean("Skincare Landing Page");
        Assert.Equal("Skincare Landing Page", result);
    }

    [Fact]
    public void TitleOver50Chars_IsTruncatedWithEllipsis()
    {
        var longTitle = new string('A', 60);
        var result = Clean(longTitle);
        Assert.NotNull(result);
        Assert.True(result!.Length <= 50);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void EmptyThinkBlock_ReturnsNull()
    {
        var raw = "<think>all content here</think>";
        var result = Clean(raw);
        Assert.Null(result);
    }

    [Fact]
    public void EmptyString_ReturnsNull()
    {
        var result = Clean("   \n  ");
        Assert.Null(result);
    }

    [Fact]
    public void MultipleLines_ReturnsFirstNonEmpty()
    {
        var raw = "\n\nFirst Line\nSecond Line";
        var result = Clean(raw);
        Assert.Equal("First Line", result);
    }

    [Fact]
    public void ExactlyFiftyChars_NotTruncated()
    {
        var title = new string('B', 50);
        var result = Clean(title);
        Assert.Equal(title, result);
    }
}
