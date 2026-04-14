using System.Reflection;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for the static design HTML validation and sanitization helpers inside
/// <see cref="ShareInvest.Agency.OpenAI.GptService"/>:
/// <list type="bullet">
/// <item><c>ValidateDesignHtml(html, expectedSectionCount)</c> — Gates 1-3, 6</item>
/// <item><c>SanitizeHtml(html)</c> — Gates 4-5</item>
/// </list>
/// Both methods are private static helpers accessed via reflection, following the
/// same convention used in <see cref="WebToolsHtmlExtractionTests"/>.
/// </summary>
public class DesignHtmlValidationTests
{
    // ─── Reflection handles ───────────────────────────────────────────────────

    static readonly Type GptServiceType =
        typeof(ShareInvest.Agency.OpenAI.GptService);

    static readonly MethodInfo ValidateDesignHtmlMethod =
        GptServiceType.GetMethod("ValidateDesignHtml",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("GptService", "ValidateDesignHtml");

    static readonly MethodInfo SanitizeHtmlMethod =
        GptServiceType.GetMethod("SanitizeHtml",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("GptService", "SanitizeHtml");

    /// <summary>Invokes ValidateDesignHtml(html, expectedSectionCount).</summary>
    static string? Validate(string html, int expectedSectionCount = 0) =>
        (string?)ValidateDesignHtmlMethod.Invoke(null, [html, expectedSectionCount]);

    /// <summary>Invokes SanitizeHtml(html).</summary>
    static string Sanitize(string html) =>
        (string)SanitizeHtmlMethod.Invoke(null, [html])!;

    // ─── Gate 1: Non-empty ────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyHtml_ReturnsError()
    {
        var error = Validate(string.Empty);

        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\r")]
    public void Validate_WhitespaceOrEmptyHtml_ReturnsNonEmptyError(string html)
    {
        var error = Validate(html);

        Assert.NotNull(error);
    }

    // ─── Gate 2: Max size (1 MB = 1,000,000 chars) ────────────────────────────

    [Fact]
    public void Validate_HtmlExceedingOneMegabyte_ReturnsError()
    {
        // Wrap in section tags so we pass gate 3, then exceed the size limit
        var bigContent = new string('A', 999_990);
        var html = $"<section>{bigContent}</section>";

        // html.Length > 1_000_000
        Assert.True(html.Length > 1_000_000);

        var error = Validate(html);

        Assert.NotNull(error);
        Assert.Contains("1,000,000", error);
    }

    [Fact]
    public void Validate_HtmlExactlyAtSizeLimit_NoSizeError()
    {
        // Build exactly 1,000,000 chars total: "<section>" (9) + filler + "</section>" (10) = 19 overhead
        const int overhead = 9 + 10; // "<section>" + "</section>"
        var html = "<section>" + new string('B', 1_000_000 - overhead) + "</section>";
        Assert.Equal(1_000_000, html.Length);

        var error = Validate(html);

        if (error is not null)
            Assert.DoesNotContain("1,000,000", error);
    }

    // ─── Gate 3: Must contain <section> tags ─────────────────────────────────

    [Fact]
    public void Validate_HtmlWithoutSectionTag_ReturnsError()
    {
        const string html = "<html><body><div>Content here</div></body></html>";

        var error = Validate(html);

        Assert.NotNull(error);
        Assert.Contains("<section>", error);
    }

    [Fact]
    public void Validate_HtmlWithSectionTag_PassesGate3()
    {
        const string html = "<html><body><section><p>Valid</p></section></body></html>";

        var error = Validate(html);

        // Gate 3 should pass — no section-tag error expected
        if (error is not null)
            Assert.DoesNotContain("must contain <section>", error);
    }

    [Fact]
    public void Validate_SectionTagCaseInsensitive_PassesGate3()
    {
        // The regex uses IgnoreCase so SECTION and Section must match
        const string html = "<html><body><SECTION><p>Content</p></SECTION></body></html>";

        var error = Validate(html);

        if (error is not null)
            Assert.DoesNotContain("must contain <section>", error);
    }

    // ─── Gate 6: Section count >= expectedSectionCount ────────────────────────

    [Fact]
    public void Validate_FewerSectionsThanExpected_ReturnsError()
    {
        // 1 section, but caller expects 3
        const string html = "<html><body><section><p>One</p></section></body></html>";

        var error = Validate(html, expectedSectionCount: 3);

        Assert.NotNull(error);
        Assert.Contains("Expected at least 3", error);
        Assert.Contains("found 1", error);
    }

    [Fact]
    public void Validate_ExactSectionCountMatch_NoCountError()
    {
        // 2 sections, caller expects 2
        const string html =
            "<html><body>" +
            "<section><p>One</p></section>" +
            "<section><p>Two</p></section>" +
            "</body></html>";

        var error = Validate(html, expectedSectionCount: 2);

        if (error is not null)
            Assert.DoesNotContain("Expected at least 2", error);
    }

    [Fact]
    public void Validate_MoreSectionsThanExpected_NoCountError()
    {
        // 3 sections, caller expects 2 — surplus sections are fine
        const string html =
            "<section>One</section>" +
            "<section>Two</section>" +
            "<section>Three</section>";

        var error = Validate(html, expectedSectionCount: 2);

        if (error is not null)
            Assert.DoesNotContain("Expected at least 2", error);
    }

    [Fact]
    public void Validate_ZeroExpectedSections_SkipsCountGate()
    {
        // expectedSectionCount=0 means gate 6 is disabled
        const string html = "<section>Only one section here.</section>";

        var error = Validate(html, expectedSectionCount: 0);

        // Gate 6 is skipped — no count error
        if (error is not null)
            Assert.DoesNotContain("Expected at least", error);
    }

    // ─── Gate 4 (Sanitize): Strip <script> tags ───────────────────────────────

    [Fact]
    public void Sanitize_ScriptTag_IsStripped()
    {
        const string html = "<div>Content</div><script>alert('xss')</script><p>After</p>";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", sanitized);
        Assert.Contains("Content", sanitized);
        Assert.Contains("After", sanitized);
    }

    [Fact]
    public void Sanitize_MultipleScriptTags_AllStripped()
    {
        const string html =
            "<script>var a=1;</script><p>Visible</p><script>document.cookie</script>";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("var a", sanitized);
        Assert.DoesNotContain("document.cookie", sanitized);
        Assert.Contains("Visible", sanitized);
    }

    [Fact]
    public void Sanitize_ScriptTagWithAttributes_IsStripped()
    {
        const string html = "<script src=\"evil.js\" type=\"text/javascript\">evilCode()</script><p>Safe</p>";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("evilCode", sanitized);
        Assert.Contains("Safe", sanitized);
    }

    // ─── Gate 5 (Sanitize): Strip on* event handlers ─────────────────────────

    [Fact]
    public void Sanitize_OnclickHandler_IsStripped()
    {
        const string html = """<button onclick="stealData()">Click me</button>""";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stealData", sanitized);
        Assert.Contains("Click me", sanitized);
    }

    [Fact]
    public void Sanitize_OnmouseoverHandler_IsStripped()
    {
        const string html = """<div onmouseover="hover()">Hover target</div>""";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("onmouseover", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hover target", sanitized);
    }

    [Fact]
    public void Sanitize_OnloadHandler_IsStripped()
    {
        const string html = """<body onload="initialize()"><p>Body content</p></body>""";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("onload", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Body content", sanitized);
    }

    [Fact]
    public void Sanitize_MultipleEventHandlers_AllStripped()
    {
        const string html =
            """<div onclick="a()" onmouseover="b()" onkeydown="c()">Multi-event</div>""";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onkeydown", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Multi-event", sanitized);
    }

    [Fact]
    public void Sanitize_HandlerWithSingleQuotedValue_IsStripped()
    {
        const string html = """<a href="#" onclick='doSomething()'>Link</a>""";

        var sanitized = Sanitize(html);

        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Sanitize + Validate integration ─────────────────────────────────────

    [Fact]
    public void SanitizeThenValidate_CleanHtml_PassesAllGates()
    {
        const string dirtyHtml =
            "<html><body>" +
            "<script>xss()</script>" +
            "<section onclick=\"steal()\"><p>Safe content here.</p></section>" +
            "</body></html>";

        var sanitized = Sanitize(dirtyHtml);
        var error = Validate(sanitized, expectedSectionCount: 1);

        // After sanitization the HTML should be valid
        Assert.Null(error);
        Assert.DoesNotContain("xss", sanitized);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Sanitize_HtmlWithNoScriptOrHandlers_UnchangedStructure()
    {
        const string html =
            "<section class=\"hero\"><h1>Title</h1><p>Paragraph text.</p></section>";

        var sanitized = Sanitize(html);

        // No scripts or handlers — tag structure preserved
        Assert.Contains("<section", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h1>Title</h1>", sanitized);
        Assert.Contains("Paragraph text.", sanitized);
    }

    [Fact]
    public void Validate_ValidMinimalHtml_ReturnsNull()
    {
        const string html = "<section><p>Minimal valid page.</p></section>";

        var error = Validate(html, expectedSectionCount: 1);

        Assert.Null(error);
    }
}
