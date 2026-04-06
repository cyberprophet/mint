using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShareInvest.Agency;

/// <summary>
/// Provider-agnostic web tools for search and URL fetching.
/// Mirrors P1 OpenCode's built-in <c>websearch</c> and <c>webfetch</c> tools.
/// </summary>
public sealed partial class WebTools : IDisposable
{
    readonly HttpClient _httpClient;
    readonly Uri _exaEndpoint;

    /// <summary>
    /// Initializes a new <see cref="WebTools"/> instance.
    /// </summary>
    /// <param name="exaApiKey">
    /// Optional Exa API key. When provided, requests are authenticated via
    /// <c>?exaApiKey={key}</c> query parameter. When <see langword="null"/>,
    /// requests are sent without authentication (matches P1 OpenCode behavior).
    /// </param>
    public WebTools(string? exaApiKey = null)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = 5 * 1024 * 1024
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        _exaEndpoint = string.IsNullOrEmpty(exaApiKey)
            ? new Uri("https://mcp.exa.ai/mcp")
            : new Uri($"https://mcp.exa.ai/mcp?exaApiKey={Uri.EscapeDataString(exaApiKey)}");
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
                    livecrawl = "fallback"
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
    /// Fetch a web page and return structured text content.
    /// Equivalent to P1's <c>webfetch</c> tool with HTML-to-text conversion.
    /// Extracts OG metadata and JSON-LD structured data before stripping HTML.
    /// </summary>
    /// <param name="url">The URL to fetch (must start with http:// or https://).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Page content as plain text with metadata header (max 50,000 characters).</returns>
    public async Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Error: URL must be a valid http:// or https:// address.";

        if (await IsPrivateHostAsync(uri.Host, cancellationToken))
            return "Error: Requests to private/internal network addresses are not allowed.";

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

        if (!contentType.StartsWith("text/") && contentType != "application/xhtml+xml")
            return $"Error: Non-text content type ({contentType}). Only text/html pages are supported.";

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // Extract structured metadata before stripping HTML
        var metadata = ExtractMetadata(html);
        var text = StripHtml(html);

        var result = new StringBuilder();

        if (metadata.Length > 0)
        {
            result.Append(metadata);
            result.Append("\n\n---\n\n");
        }

        result.Append(text);

        const int maxChars = 50_000;

        if (result.Length > maxChars)
        {
            result.Length = maxChars;
            result.Append("\n[Content truncated]");
        }

        return result.ToString();
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
            // DNS resolution failure — allow request to proceed and let HttpClient handle the error
        }

        return false;
    }

    /// <summary>
    /// Extracts Open Graph meta tags and JSON-LD structured data from HTML
    /// before the main content is stripped. Returns a summary header.
    /// </summary>
    static string ExtractMetadata(string html)
    {
        var parts = new List<string>();

        // Extract OG meta tags: <meta property="og:..." content="...">
        foreach (Match m in OgMetaRegex().Matches(html))
        {
            var property = m.Groups[1].Value;
            var content = WebUtility.HtmlDecode(m.Groups[2].Value);
            parts.Add($"{property}: {content}");
        }

        // Extract JSON-LD blocks: <script type="application/ld+json">...</script>
        foreach (Match m in JsonLdRegex().Matches(html))
        {
            var jsonLd = m.Groups[1].Value.Trim();

            if (jsonLd.Length > 5000)
                jsonLd = jsonLd[..5000] + "...";

            parts.Add($"[JSON-LD] {jsonLd}");
        }

        return parts.Count > 0
            ? $"[Metadata]\n{string.Join("\n", parts)}"
            : "";
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
    public void Dispose() => _httpClient.Dispose();
}
