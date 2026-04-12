using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency;

/// <summary>
/// Provider-agnostic web tools for search and URL fetching.
/// Mirrors P1 OpenCode's built-in <c>websearch</c> and <c>webfetch</c> tools.
/// </summary>
public sealed partial class WebTools : ISearchProvider, IDisposable
{
    readonly HttpClient _httpClient;
    readonly HttpClient _fetchClient;
    readonly Uri _exaEndpoint;
    readonly string? _exaApiKey;

    const int MaxRedirects = 5;

    /// <summary>
    /// Initializes a new <see cref="WebTools"/> instance.
    /// </summary>
    /// <param name="exaApiKey">
    /// Optional Exa API key. When provided, requests are authenticated via the
    /// <c>x-api-key</c> HTTP header. When <see langword="null"/>, requests are
    /// sent without authentication (matches P1 OpenCode behavior).
    /// </param>
    public WebTools(string? exaApiKey = null)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 5 * 1024 * 1024
        };

        // Separate client for FetchAsync with auto-redirect disabled
        // to re-validate each redirect hop against the SSRF deny-list.
        _fetchClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 5 * 1024 * 1024
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        _fetchClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        _exaEndpoint = new Uri("https://mcp.exa.ai/mcp");
        _exaApiKey = string.IsNullOrEmpty(exaApiKey) ? null : exaApiKey;
    }

    /// <summary>
    /// Web search via Exa MCP endpoint (JSON-RPC 2.0).
    /// Equivalent to P1's <c>websearch</c> tool calling <c>web_search_exa</c>.
    /// Authenticates only when an API key was provided to the constructor.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="numResults">Number of results to return (default: 8).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results as text.</returns>
    public async Task<string> SearchAsync(string query, int numResults = 8, CancellationToken cancellationToken = default)
    {
        var jsonRpcRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "web_search_exa",
                arguments = new
                {
                    query,
                    type = "auto",
                    numResults,
                    livecrawl = "fallback",
                    contents = new
                    {
                        text = new { maxCharacters = 3000 }
                    }
                }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(jsonRpcRequest),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, _exaEndpoint)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_exaApiKey is not null)
            request.Headers.TryAddWithoutValidation("x-api-key", _exaApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        foreach (var line in responseText.Split('\n'))
        {
            if (line.StartsWith("data: "))
            {
                using var sseDoc = JsonDocument.Parse(line[6..]);

                if (sseDoc.RootElement.TryGetProperty("result", out var resultProp)
                    && resultProp.TryGetProperty("content", out var contentArr)
                    && contentArr.GetArrayLength() > 0)
                {
                    return contentArr[0].GetProperty("text").GetString() ?? "No results";
                }
            }
        }

        return "No search results found.";
    }

    /// <summary>
    /// Fetch a web page and return a structured <see cref="FetchResult"/> with separated metadata and body text.
    /// Equivalent to P1's <c>webfetch</c> tool with HTML-to-text conversion.
    /// Extracts OG metadata and JSON-LD structured data before stripping HTML.
    /// </summary>
    /// <param name="url">The URL to fetch (must start with http:// or https://).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured <see cref="FetchResult"/> with page metadata and body text.</returns>
    /// <exception cref="InvalidOperationException">Thrown for SSRF-blocked URLs, non-text content, or too many redirects.</exception>
    public async Task<FetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new InvalidOperationException("URL must be a valid http:// or https:// address.");

        if (await IsPrivateHostAsync(uri.Host, cancellationToken))
            throw new InvalidOperationException("Requests to private/internal network addresses are not allowed.");

        // Manual redirect loop — re-validate each hop against the SSRF deny-list.
        var currentUri = uri;
        var warnings = new List<string>();

        HttpResponseMessage response;

        for (int hop = 0; ; hop++)
        {
            response = await _fetchClient.GetAsync(currentUri, cancellationToken);

            if ((int)response.StatusCode is >= 301 and <= 308
                && response.Headers.Location is { } location)
            {
                if (hop >= MaxRedirects)
                    throw new InvalidOperationException($"Too many redirects (exceeded {MaxRedirects}).");

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);

                if (nextUri.Scheme != "http" && nextUri.Scheme != "https")
                    throw new InvalidOperationException($"Redirect to unsupported scheme ({nextUri.Scheme}).");

                if (await IsPrivateHostAsync(nextUri.Host, cancellationToken))
                    throw new InvalidOperationException("Redirect target resolves to a private/internal network address.");

                currentUri = nextUri;
                response.Dispose();
                continue;
            }

            break;
        }

        // Cloudflare bot detection: 403 with cf-mitigated header → retry with honest UA (P1 parity)
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden
            && response.Headers.TryGetValues("cf-mitigated", out var cfValues)
            && cfValues.Any(v => v.Contains("challenge", StringComparison.OrdinalIgnoreCase)))
        {
            response.Dispose();
            warnings.Add("Cloudflare challenge detected — retried with page-mint-agency user agent");

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, currentUri);
            retryRequest.Headers.UserAgent.Clear();
            retryRequest.Headers.UserAgent.ParseAdd("page-mint-agency");

            response = await _fetchClient.SendAsync(retryRequest, cancellationToken);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            var statusCode = (int)response.StatusCode;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            if (!contentType.StartsWith("text/") && contentType != "application/xhtml+xml")
                throw new InvalidOperationException($"Non-text content type ({contentType}). Only text-based pages are supported.");

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Extract structured metadata fields
            var title = ExtractMetaField(html, "og:title") ?? ExtractTitleTag(html);
            var metaDescription = ExtractMetaField(html, "og:description") ?? ExtractMetaDescription(html);
            var ogImage = ExtractMetaField(html, "og:image");
            var jsonLd = ExtractJsonLdText(html);

            var text = StripHtml(html);

            const int maxChars = 8_000;
            bool truncated = text.Length > maxChars;

            if (truncated)
            {
                text = text[..maxChars];
                warnings.Add("Content truncated to 8,000 characters");
            }

            return new FetchResult(
                FinalUrl: currentUri.ToString(),
                StatusCode: statusCode,
                Title: title,
                MetaDescription: metaDescription,
                OgImage: ogImage,
                JsonLd: jsonLd,
                MainText: text,
                Warnings: warnings.Count > 0 ? [.. warnings] : null);
        }
    }

    /// <summary>
    /// Checks whether a hostname resolves to a private, loopback, or link-local IP address.
    /// Prevents SSRF attacks targeting internal infrastructure.
    /// </summary>
    static async Task<bool> IsPrivateHostAsync(string host, CancellationToken cancellationToken)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

            foreach (var addr in addresses)
            {
                if (IPAddress.IsLoopback(addr))
                    return true;

                var bytes = addr.GetAddressBytes();

                if (addr.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
                {
                    // 10.0.0.0/8
                    if (bytes[0] == 10) return true;
                    // 172.16.0.0/12
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                    // 192.168.0.0/16
                    if (bytes[0] == 192 && bytes[1] == 168) return true;
                    // 169.254.0.0/16 (link-local / cloud metadata)
                    if (bytes[0] == 169 && bytes[1] == 254) return true;
                    // 0.0.0.0/8
                    if (bytes[0] == 0) return true;
                }

                if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal)
                        return true;
                }
            }
        }
        catch
        {
            // DNS resolution failure — treat as private/blocked for safety.
            // If DNS can't resolve, we shouldn't allow the request to proceed unchecked.
            return true;
        }

        return false;
    }

    /// <summary>Extracts the value of a named OG meta property from HTML.</summary>
    static string? ExtractMetaField(string html, string property)
    {
        foreach (Match m in OgMetaRegex().Matches(html))
        {
            if (string.Equals(m.Groups[1].Value, property, StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(m.Groups[2].Value);
        }

        return null;
    }

    /// <summary>Extracts the &lt;title&gt; tag content from HTML.</summary>
    static string? ExtractTitleTag(string html)
    {
        var m = TitleTagRegex().Match(html);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
    }

    /// <summary>Extracts the standard meta description from HTML.</summary>
    static string? ExtractMetaDescription(string html)
    {
        var m = MetaDescriptionRegex().Match(html);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    /// <summary>Extracts and concatenates all JSON-LD script blocks from HTML.</summary>
    static string? ExtractJsonLdText(string html)
    {
        var parts = new List<string>();

        foreach (Match m in JsonLdRegex().Matches(html))
        {
            var jsonLd = m.Groups[1].Value.Trim();

            if (jsonLd.Length > 5000)
                jsonLd = jsonLd[..5000] + "...";

            parts.Add(jsonLd);
        }

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    static string StripHtml(string html)
    {
        var cleaned = ScriptBlockRegex().Replace(html, "");
        cleaned = StyleBlockRegex().Replace(cleaned, "");
        cleaned = BlockElementRegex().Replace(cleaned, "\n");
        cleaned = HtmlTagRegex().Replace(cleaned, "");
        cleaned = WebUtility.HtmlDecode(cleaned);
        cleaned = HorizontalWhitespaceRegex().Replace(cleaned, " ");
        cleaned = ExcessiveNewlineRegex().Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }

    [GeneratedRegex(@"<meta\s+property=""(og:[^""]+)""\s+content=""([^""]*)""\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex OgMetaRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTagRegex();

    [GeneratedRegex(@"<meta\s+name=""description""\s+content=""([^""]*)""\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescriptionRegex();

    [GeneratedRegex(@"<script\s+type=""application/ld\+json""[^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdRegex();

    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlockRegex();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockRegex();

    [GeneratedRegex(@"<(br|p|div|li|tr|h[1-6])[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElementRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlineRegex();

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
        _fetchClient.Dispose();
    }
}
