using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShareInvest.Agency;

/// <summary>
/// Provider-agnostic web tools for search and URL fetching.
/// Mirrors P1 OpenCode's built-in <c>websearch</c> and <c>webfetch</c> tools.
/// </summary>
public sealed class WebTools : IDisposable
{
    readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new <see cref="WebTools"/> instance with a pre-configured <see cref="HttpClient"/>.
    /// </summary>
    public WebTools()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// Web search via Exa MCP endpoint (JSON-RPC 2.0, no API key required).
    /// Equivalent to P1's <c>websearch</c> tool calling <c>web_search_exa</c>.
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

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://mcp.exa.ai/mcp")
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        // Parse SSE response: find "data: {...}" line
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
    /// Fetch a web page and return plain text content.
    /// Equivalent to P1's <c>webfetch</c> tool with HTML-to-text conversion.
    /// </summary>
    /// <param name="url">The URL to fetch (must start with http:// or https://).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Page content as plain text (max 50,000 characters).</returns>
    public async Task<string> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            return "Error: URL must start with http:// or https://";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > 5 * 1024 * 1024)
            return "Error: Response too large (exceeds 5MB)";

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        if (html.Length > 5 * 1024 * 1024)
            return "Error: Response too large (exceeds 5MB)";

        var text = StripHtml(html);

        const int maxChars = 50_000;

        if (text.Length > maxChars)
            text = text[..maxChars] + "\n[Content truncated]";

        return text;
    }

    static string StripHtml(string html)
    {
        // Remove script and style blocks
        var cleaned = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // Replace block elements with newlines
        cleaned = Regex.Replace(cleaned, @"<(br|p|div|li|tr|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);

        // Strip remaining tags
        cleaned = Regex.Replace(cleaned, @"<[^>]+>", "");

        // Decode HTML entities
        cleaned = WebUtility.HtmlDecode(cleaned);

        // Collapse whitespace
        cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");

        return cleaned.Trim();
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
