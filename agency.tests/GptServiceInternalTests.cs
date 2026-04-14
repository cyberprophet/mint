using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.OpenAI;

using System.Reflection;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Tests for private static helper methods in <see cref="GptService"/> via reflection.
/// These exercise the actual generated regex objects (ThinkBlockRegex) for coverage,
/// which the inline-duplicate TitleCleanupTests cannot reach.
/// </summary>
public class GptServiceInternalTests
{
    static readonly GptService Sut = new(NullLogger<GptService>.Instance, "test-key");

    // Resolve the private static CleanTitleResponse method once.
    static readonly MethodInfo CleanTitleResponseMethod =
        typeof(GptService).GetMethod("CleanTitleResponse", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(GptService), "CleanTitleResponse");

    static string? CleanTitle(string raw) =>
        (string?)CleanTitleResponseMethod.Invoke(null, [raw]);

    // ─── CleanTitleResponse (exercises ThinkBlockRegex generated class) ─────────

    [Fact]
    public void CleanTitleResponse_PlainTitle_ReturnsTrimmedTitle()
    {
        var result = CleanTitle("Skincare Landing Page");
        Assert.Equal("Skincare Landing Page", result);
    }

    [Fact]
    public void CleanTitleResponse_ThinkBlockRemoved_ReturnsFollowingLine()
    {
        var result = CleanTitle("<think>reasoning here</think>\nActual Title");
        Assert.Equal("Actual Title", result);
    }

    [Fact]
    public void CleanTitleResponse_MultilineThinkBlock_AllRemovedBeforeTitle()
    {
        var result = CleanTitle("<think>\nline1\nline2\n</think>\nClean Title");
        Assert.Equal("Clean Title", result);
    }

    [Fact]
    public void CleanTitleResponse_OnlyThinkBlock_ReturnsNull()
    {
        var result = CleanTitle("<think>only think content</think>");
        Assert.Null(result);
    }

    [Fact]
    public void CleanTitleResponse_EmptyString_ReturnsNull()
    {
        var result = CleanTitle(string.Empty);
        Assert.Null(result);
    }

    [Fact]
    public void CleanTitleResponse_WhitespaceOnly_ReturnsNull()
    {
        var result = CleanTitle("   \n   \n  ");
        Assert.Null(result);
    }

    [Fact]
    public void CleanTitleResponse_TitleOver50Chars_TruncatesWithEllipsis()
    {
        var longTitle = new string('X', 60);
        var result = CleanTitle(longTitle);

        Assert.NotNull(result);
        Assert.True(result!.Length <= 50);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void CleanTitleResponse_TitleExactly50Chars_NotTruncated()
    {
        var title = new string('Z', 50);
        var result = CleanTitle(title);
        Assert.Equal(title, result);
    }

    [Fact]
    public void CleanTitleResponse_MultipleLines_ReturnsFirstNonEmpty()
    {
        var result = CleanTitle("\n\nFirst Non-Empty Line\nSecond Line");
        Assert.Equal("First Non-Empty Line", result);
    }

    [Fact]
    public void CleanTitleResponse_WithLeadingAndTrailingWhitespaceInLine_TrimmedCorrectly()
    {
        // Each line is trimmed via .Trim() in the LINQ chain
        var result = CleanTitle("  Hello World  ");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void CleanTitleResponse_ThinkBlockFollowedByEmpty_ReturnsNull()
    {
        var result = CleanTitle("<think>content</think>\n\n   \n  ");
        Assert.Null(result);
    }
}
