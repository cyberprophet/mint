using System.Reflection;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for the private static HTML extraction helpers in <see cref="WebTools"/>.
/// Methods are invoked via reflection because they are private implementation details
/// of the sealed class — source code must not be modified.
/// </summary>
public class WebToolsHtmlExtractionTests
{
    // Reflected method handles — resolved once per test class lifetime.
    static readonly MethodInfo ExtractMetaField =
        typeof(WebTools).GetMethod("ExtractMetaField", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(WebTools), "ExtractMetaField");

    static readonly MethodInfo ExtractTitleTag =
        typeof(WebTools).GetMethod("ExtractTitleTag", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(WebTools), "ExtractTitleTag");

    static readonly MethodInfo ExtractMetaDescription =
        typeof(WebTools).GetMethod("ExtractMetaDescription", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(WebTools), "ExtractMetaDescription");

    static readonly MethodInfo ExtractJsonLdText =
        typeof(WebTools).GetMethod("ExtractJsonLdText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(WebTools), "ExtractJsonLdText");

    static readonly MethodInfo StripHtml =
        typeof(WebTools).GetMethod("StripHtml", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(WebTools), "StripHtml");

    // Helpers to invoke reflected methods and unwrap TargetInvocationException.
    static string? InvokeExtractMetaField(string html, string property) =>
        (string?)ExtractMetaField.Invoke(null, [html, property]);

    static string? InvokeExtractTitleTag(string html) =>
        (string?)ExtractTitleTag.Invoke(null, [html]);

    static string? InvokeExtractMetaDescription(string html) =>
        (string?)ExtractMetaDescription.Invoke(null, [html]);

    static string? InvokeExtractJsonLdText(string html) =>
        (string?)ExtractJsonLdText.Invoke(null, [html]);

    static string InvokeStripHtml(string html) =>
        (string?)StripHtml.Invoke(null, [html]) ?? string.Empty;

    // ─── OG Metadata Extraction ───────────────────────────────────────────────

    [Fact]
    public void ExtractMetaField_OgTitle_ReturnsValue()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="My Page Title" />
            </head></html>
            """;

        var result = InvokeExtractMetaField(html, "og:title");

        Assert.Equal("My Page Title", result);
    }

    [Fact]
    public void ExtractMetaField_OgDescription_ReturnsValue()
    {
        const string html = """
            <html><head>
            <meta property="og:description" content="A concise summary." />
            </head></html>
            """;

        var result = InvokeExtractMetaField(html, "og:description");

        Assert.Equal("A concise summary.", result);
    }

    [Fact]
    public void ExtractMetaField_OgImage_ReturnsValue()
    {
        const string html = """
            <html><head>
            <meta property="og:image" content="https://example.com/cover.jpg" />
            </head></html>
            """;

        var result = InvokeExtractMetaField(html, "og:image");

        Assert.Equal("https://example.com/cover.jpg", result);
    }

    [Fact]
    public void ExtractMetaField_IsCaseInsensitiveOnProperty()
    {
        // The regex uses IgnoreCase — property attribute casing must not matter.
        const string html = """<meta property="OG:TITLE" content="Caps Title" />""";

        var result = InvokeExtractMetaField(html, "og:title");

        Assert.Equal("Caps Title", result);
    }

    [Fact]
    public void ExtractMetaField_HtmlEncodedContent_IsDecoded()
    {
        const string html = """<meta property="og:title" content="Tom &amp; Jerry" />""";

        var result = InvokeExtractMetaField(html, "og:title");

        Assert.Equal("Tom & Jerry", result);
    }

    [Fact]
    public void ExtractMetaField_MissingProperty_ReturnsNull()
    {
        const string html = """<html><head><title>No OG Here</title></head></html>""";

        var result = InvokeExtractMetaField(html, "og:title");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractMetaField_EmptyHtml_ReturnsNull()
    {
        var result = InvokeExtractMetaField(string.Empty, "og:title");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractMetaField_MultipleOgProperties_ReturnsCorrectOne()
    {
        const string html = """
            <meta property="og:title" content="The Title" />
            <meta property="og:description" content="The Desc" />
            <meta property="og:image" content="https://img.example.com/x.png" />
            """;

        Assert.Equal("The Title", InvokeExtractMetaField(html, "og:title"));
        Assert.Equal("The Desc", InvokeExtractMetaField(html, "og:description"));
        Assert.Equal("https://img.example.com/x.png", InvokeExtractMetaField(html, "og:image"));
    }

    // ─── Title Tag Extraction ─────────────────────────────────────────────────

    [Fact]
    public void ExtractTitleTag_WellFormed_ReturnsTitle()
    {
        const string html = "<html><head><title>Hello World</title></head></html>";

        var result = InvokeExtractTitleTag(html);

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void ExtractTitleTag_WithAttributes_ReturnsTitle()
    {
        // title tags rarely have attributes but the regex handles them.
        const string html = "<title lang=\"en\">Attributed Title</title>";

        var result = InvokeExtractTitleTag(html);

        Assert.Equal("Attributed Title", result);
    }

    [Fact]
    public void ExtractTitleTag_HtmlEncodedContent_IsDecoded()
    {
        const string html = "<title>Rock &amp; Roll &lt;Live&gt;</title>";

        var result = InvokeExtractTitleTag(html);

        Assert.Equal("Rock & Roll <Live>", result);
    }

    [Fact]
    public void ExtractTitleTag_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        const string html = "<title>  Padded Title  </title>";

        var result = InvokeExtractTitleTag(html);

        Assert.Equal("Padded Title", result);
    }

    [Fact]
    public void ExtractTitleTag_NoTitleTag_ReturnsNull()
    {
        const string html = "<html><head></head><body>No title here</body></html>";

        var result = InvokeExtractTitleTag(html);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractTitleTag_EmptyHtml_ReturnsNull()
    {
        var result = InvokeExtractTitleTag(string.Empty);

        Assert.Null(result);
    }

    // ─── Meta Description Extraction ──────────────────────────────────────────

    [Fact]
    public void ExtractMetaDescription_WellFormed_ReturnsDescription()
    {
        const string html = """<meta name="description" content="Site summary text." />""";

        var result = InvokeExtractMetaDescription(html);

        Assert.Equal("Site summary text.", result);
    }

    [Fact]
    public void ExtractMetaDescription_HtmlEncoded_IsDecoded()
    {
        const string html = """<meta name="description" content="A &amp; B" />""";

        var result = InvokeExtractMetaDescription(html);

        Assert.Equal("A & B", result);
    }

    [Fact]
    public void ExtractMetaDescription_Missing_ReturnsNull()
    {
        const string html = "<html><head><title>No desc</title></head></html>";

        var result = InvokeExtractMetaDescription(html);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractMetaDescription_EmptyHtml_ReturnsNull()
    {
        var result = InvokeExtractMetaDescription(string.Empty);

        Assert.Null(result);
    }

    // ─── JSON-LD Extraction ───────────────────────────────────────────────────

    [Fact]
    public void ExtractJsonLdText_SingleBlock_ReturnsContent()
    {
        const string html = """
            <script type="application/ld+json">
            {"@type":"Product","name":"Widget"}
            </script>
            """;

        var result = InvokeExtractJsonLdText(html);

        Assert.NotNull(result);
        Assert.Contains("@type", result);
        Assert.Contains("Widget", result);
    }

    [Fact]
    public void ExtractJsonLdText_MultipleBlocks_ConcatenatesAll()
    {
        const string html = """
            <script type="application/ld+json">{"@type":"Product"}</script>
            <script type="application/ld+json">{"@type":"Organization"}</script>
            """;

        var result = InvokeExtractJsonLdText(html);

        Assert.NotNull(result);
        Assert.Contains("Product", result);
        Assert.Contains("Organization", result);
    }

    [Fact]
    public void ExtractJsonLdText_LargeBlock_IsTruncatedAt5000Chars()
    {
        var bigJson = new string('x', 6000);
        var html = $"""<script type="application/ld+json">{bigJson}</script>""";

        var result = InvokeExtractJsonLdText(html);

        Assert.NotNull(result);
        // Truncated to 5000 chars + "..."
        Assert.EndsWith("...", result);
        Assert.True(result!.Length <= 5003);
    }

    [Fact]
    public void ExtractJsonLdText_NoJsonLdBlock_ReturnsNull()
    {
        const string html = "<html><head></head><body>No JSON-LD here.</body></html>";

        var result = InvokeExtractJsonLdText(html);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractJsonLdText_EmptyHtml_ReturnsNull()
    {
        var result = InvokeExtractJsonLdText(string.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractJsonLdText_TypeAttributeCaseInsensitive_Matches()
    {
        // Regex has IgnoreCase — TYPE="APPLICATION/LD+JSON" should still match.
        const string html = """<script TYPE="APPLICATION/LD+JSON">{"key":"val"}</script>""";

        var result = InvokeExtractJsonLdText(html);

        Assert.NotNull(result);
        Assert.Contains("val", result);
    }

    // ─── StripHtml / Text Extraction ─────────────────────────────────────────

    [Fact]
    public void StripHtml_PlainParagraphs_ExtractsText()
    {
        const string html = "<p>Hello</p><p>World</p>";

        var result = InvokeStripHtml(html);

        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
    }

    [Fact]
    public void StripHtml_RemovesScriptBlocks()
    {
        const string html = "<p>Visible</p><script>alert('xss')</script><p>Also visible</p>";

        var result = InvokeStripHtml(html);

        Assert.DoesNotContain("alert", result);
        Assert.Contains("Visible", result);
        Assert.Contains("Also visible", result);
    }

    [Fact]
    public void StripHtml_RemovesStyleBlocks()
    {
        const string html = "<p>Content</p><style>body { color: red; }</style>";

        var result = InvokeStripHtml(html);

        Assert.DoesNotContain("color:", result);
        Assert.Contains("Content", result);
    }

    [Fact]
    public void StripHtml_DecodesHtmlEntities()
    {
        const string html = "<p>Tom &amp; Jerry &lt;3</p>";

        var result = InvokeStripHtml(html);

        Assert.Contains("Tom & Jerry", result);
    }

    [Fact]
    public void StripHtml_BlockElements_InsertNewlines()
    {
        const string html = "<div>First</div><div>Second</div>";

        var result = InvokeStripHtml(html);

        // Block elements replaced with \n, so both words should appear on separate lines.
        Assert.Contains("First", result);
        Assert.Contains("Second", result);
        Assert.Contains('\n', result);
    }

    [Fact]
    public void StripHtml_ExcessiveNewlines_AreCollapsed()
    {
        const string html = "<div>A</div>\n\n\n\n<div>B</div>";

        var result = InvokeStripHtml(html);

        // Three or more consecutive newlines must be collapsed to two.
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void StripHtml_EmptyString_ReturnsEmpty()
    {
        var result = InvokeStripHtml(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void StripHtml_MalformedTags_DoesNotThrow()
    {
        const string html = "<div unclosed <p>Text</div><<< garbage >>>";

        // Must not throw — malformed HTML is handled gracefully.
        var result = InvokeStripHtml(html);
        Assert.NotNull(result);
    }

    // ─── OG → Title tag fallback (integration of ExtractMetaField + ExtractTitleTag) ──

    [Fact]
    public void OgTitle_TakesPrecedenceOver_TitleTag()
    {
        // When both og:title and <title> are present, og:title wins.
        const string html = """
            <html><head>
            <title>HTML Title</title>
            <meta property="og:title" content="OG Title" />
            </head></html>
            """;

        var ogTitle = InvokeExtractMetaField(html, "og:title");
        var htmlTitle = InvokeExtractTitleTag(html);

        // Caller logic: og:title ?? <title>
        var effective = ogTitle ?? htmlTitle;

        Assert.Equal("OG Title", effective);
    }

    [Fact]
    public void OgTitle_FallsBackTo_TitleTag_WhenAbsent()
    {
        const string html = """
            <html><head>
            <title>Fallback Title</title>
            </head></html>
            """;

        var ogTitle = InvokeExtractMetaField(html, "og:title");
        var htmlTitle = InvokeExtractTitleTag(html);

        var effective = ogTitle ?? htmlTitle;

        Assert.Equal("Fallback Title", effective);
    }
}
