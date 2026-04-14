using ShareInvest.Agency.Models;

using System.Net;
using System.Reflection;
using System.Text;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Tests for <see cref="WebTools.FetchAsync"/> logic that requires mocked HTTP responses.
/// Uses reflection to inject a <see cref="FakeHttpMessageHandler"/> into the private
/// <c>_fetchClient</c> field, bypassing network I/O while exercising the fetch loop,
/// redirect handling, Cloudflare retry, and content-type guard branches.
/// </summary>
public class WebToolsFetchTests : IDisposable
{
    readonly WebTools _webTools;

    // Retrieve private field info once.
    static readonly FieldInfo FetchClientField =
        typeof(WebTools).GetField("_fetchClient", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(nameof(WebTools), "_fetchClient");

    public WebToolsFetchTests()
    {
        _webTools = new WebTools();
    }

    /// <summary>
    /// Replaces the private _fetchClient with one backed by <paramref name="handler"/>.
    /// </summary>
    void InjectFetchClient(FakeHttpMessageHandler handler)
    {
        var existing = (HttpClient)FetchClientField.GetValue(_webTools)!;
        existing.Dispose();

        var newClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 5 * 1024 * 1024
        };
        newClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        FetchClientField.SetValue(_webTools, newClient);
    }

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_SimpleHtmlPage_ReturnsParsedResult()
    {
        const string html = """
            <html><head>
            <title>Test Page</title>
            <meta name="description" content="A test page" />
            </head><body><p>Hello World</p></body></html>
            """;

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/page",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Test Page", result.Title);
        Assert.Equal("A test page", result.MetaDescription);
        Assert.Contains("Hello World", result.MainText);
        Assert.Null(result.Warnings);
    }

    [Fact]
    public async Task FetchAsync_WithOgTags_ExtractsOgMetadata()
    {
        const string html = """
            <html><head>
            <meta property="og:title" content="OG Title" />
            <meta property="og:description" content="OG description" />
            <meta property="og:image" content="https://cdn.example.com/img.jpg" />
            </head><body><p>Content</p></body></html>
            """;

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.Equal("OG Title", result.Title);
        Assert.Equal("OG description", result.MetaDescription);
        Assert.Equal("https://cdn.example.com/img.jpg", result.OgImage);
    }

    [Fact]
    public async Task FetchAsync_WithJsonLd_ExtractsStructuredData()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{"@type":"Product","name":"Widget"}</script>
            </head><body><p>Content</p></body></html>
            """;

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.JsonLd);
        Assert.Contains("Widget", result.JsonLd!);
    }

    // ─── Content truncation ───────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_LargeContent_TruncatesAt8000CharsWithWarning()
    {
        var bigText = "<p>" + new string('A', 9000) + "</p>";
        var html = $"<html><body>{bigText}</body></html>";

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.True(result.MainText.Length <= 8000,
            $"Expected MainText <= 8000 chars, got {result.MainText.Length}");
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings!, w => w.Contains("truncated"));
    }

    // ─── Non-text content type guard ──────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_NonTextContentType_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xFE])
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
                }
            });

        InjectFetchClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync("https://example.com/image.png",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAsync_ApplicationXhtmlContentType_IsAllowed()
    {
        const string html = "<html><body><p>XHTML content</p></body></html>";

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "application/xhtml+xml")
            });

        InjectFetchClient(handler);

        // application/xhtml+xml is explicitly allowed — must not throw.
        var result = await _webTools.FetchAsync("https://example.com/xhtml",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Contains("XHTML content", result.MainText);
    }

    // ─── HTTP error response ──────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_Http500Response_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        InjectFetchClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _webTools.FetchAsync("https://example.com/error",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAsync_Http404Response_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound));

        InjectFetchClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _webTools.FetchAsync("https://example.com/missing",
                TestContext.Current.CancellationToken));
    }

    // ─── Redirect handling ────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_SingleRedirect_FollowsAndReturnsResult()
    {
        const string html = "<html><body><p>Final page</p></body></html>";

        // Sequence: 302 → 200
        var step = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            step++;
            if (step == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://example.com/final");
                return redirect;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/original",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("https://example.com/final", result.FinalUrl);
        Assert.Contains("Final page", result.MainText);
    }

    [Fact]
    public async Task FetchAsync_TooManyRedirects_ThrowsInvalidOperationException()
    {
        // Always returns a redirect — should exhaust MaxRedirects (5) and throw.
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            redirect.Headers.Location = new Uri("https://example.com/loop");
            return redirect;
        });

        InjectFetchClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync("https://example.com/loop",
                TestContext.Current.CancellationToken));

        Assert.Contains("redirect", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_RedirectToNonHttpScheme_ThrowsInvalidOperationException()
    {
        // Redirect to ftp:// must be blocked.
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            redirect.Headers.Location = new Uri("ftp://files.example.com/data.zip");
            return redirect;
        });

        InjectFetchClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync("https://example.com/",
                TestContext.Current.CancellationToken));

        Assert.Contains("scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Cloudflare challenge retry ───────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_CloudflareChallenge_RetriesWithAlternateUserAgent()
    {
        const string html = "<html><body><p>Content after Cloudflare</p></body></html>";
        var step = 0;

        var handler = new FakeHttpMessageHandler(_ =>
        {
            step++;
            if (step == 1)
            {
                // First call: 403 with cf-mitigated: challenge
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
                response.Headers.TryAddWithoutValidation("cf-mitigated", "challenge");
                return response;
            }
            // Second call (retry): 200 OK
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
        });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Contains("Content after Cloudflare", result.MainText);
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings!, w => w.Contains("Cloudflare"));
    }

    // ─── Final URL tracking ───────────────────────────────────────────────────

    [Fact]
    public async Task FetchAsync_NoRedirect_FinalUrlMatchesInput()
    {
        const string html = "<html><body>ok</body></html>";

        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        InjectFetchClient(handler);

        var result = await _webTools.FetchAsync("https://example.com/page",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://example.com/page", result.FinalUrl);
    }

    public void Dispose() => _webTools.Dispose();

    /// <summary>
    /// Minimal fake <see cref="HttpMessageHandler"/> that returns a canned or computed response.
    /// </summary>
    sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public FakeHttpMessageHandler(HttpResponseMessage response)
            : this(_ => response) { }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_factory(request));
    }
}
