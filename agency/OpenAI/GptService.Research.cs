using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    static readonly Lazy<string> librarianSystemPrompt = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ShareInvest.Agency.Prompts.librarian-system.md")
            ?? throw new InvalidOperationException("librarian-system.md embedded resource not found.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });

    /// <summary>
    /// Conducts multi-step web research on a product and returns structured market insights.
    /// </summary>
    /// <param name="productInfo">Product name or description to research.</param>
    /// <param name="urls">Reference URLs to fetch and analyze (product pages, brand sites, etc.).</param>
    /// <param name="category">Optional product category hint to guide research focus.</param>
    /// <param name="model">Chat model to use for the research agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed <see cref="ResearchResult"/>, or <see langword="null"/> if the agent did not return valid JSON.</returns>
    public virtual async Task<ResearchResult?> ResearchProductAsync(
        string productInfo,
        string[] urls,
        string? category,
        string model = "gpt-5.4-nano",
        CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient(model);

        // Define tools matching P1's websearch and webfetch
        var searchTool = ChatTool.CreateFunctionTool(
            "web_search_exa",
            "Search the web for information. Returns search results with titles, URLs, and content snippets.",
            BinaryData.FromString(JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query" },
                    numResults = new { type = "integer", description = "Number of results (default: 8)" }
                },
                required = new[] { "query" }
            })));

        var fetchTool = ChatTool.CreateFunctionTool(
            "web_fetch",
            "Fetch the content of a web page URL. Returns the page content as plain text.",
            BinaryData.FromString(JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    url = new { type = "string", description = "The URL to fetch" }
                },
                required = new[] { "url" }
            })));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 4096,
            Temperature = 0.1f
        };
        options.Tools.Add(searchTool);
        options.Tools.Add(fetchTool);

        // Build user message
        var userContent = new StringBuilder($"Research this product: {productInfo}");

        if (!string.IsNullOrEmpty(category))
            userContent.Append($"\nProduct category: {category}");

        if (urls.Length > 0)
        {
            userContent.Append("\n\nReference URLs to analyze:");

            for (int i = 0; i < urls.Length; i++)
                userContent.Append($"\n{i + 1}. {urls[i]}");
        }

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(librarianSystemPrompt.Value),
            ChatMessage.CreateUserMessage(userContent.ToString())
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        const int maxIterations = 10;

        for (int i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = result.Value;

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                // Add assistant message with tool calls
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                // Process each tool call
                foreach (var toolCall in completion.ToolCalls)
                {
                    string toolResult;

                    try
                    {
                        if (toolCall.FunctionName == "web_search_exa")
                        {
                            toolResult = await CallExaSearchAsync(httpClient, toolCall.FunctionArguments, cancellationToken);
                        }
                        else if (toolCall.FunctionName == "web_fetch")
                        {
                            toolResult = await CallWebFetchAsync(httpClient, toolCall.FunctionArguments, cancellationToken);
                        }
                        else
                        {
                            toolResult = $"Unknown tool: {toolCall.FunctionName}";
                        }
                    }
                    catch (Exception ex)
                    {
                        toolResult = $"Error: {ex.Message}";
                        logger.LogWarning(ex, "Tool call {ToolName} failed", toolCall.FunctionName);
                    }

                    messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, toolResult));
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                // Final response — parse JSON
                var raw = completion.Content.FirstOrDefault()?.Text;

                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                return ParseResearchResult(raw);
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        logger.LogWarning("Research loop exhausted {Max} iterations without final answer", maxIterations);
        return null;
    }

    /// <summary>
    /// Call Exa MCP web_search_exa via direct HTTP POST (same as P1 OpenCode).
    /// No API key required.
    /// </summary>
    async Task<string> CallExaSearchAsync(HttpClient httpClient, BinaryData arguments, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(arguments.ToString());
        var root = doc.RootElement;

        var query = root.GetProperty("query").GetString() ?? "";
        var numResults = root.TryGetProperty("numResults", out var nrProp) ? nrProp.GetInt32() : 8;

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

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        // Parse SSE response: find "data: {...}" line
        foreach (var line in responseText.Split('\n'))
        {
            if (line.StartsWith("data: "))
            {
                var data = line[6..];

                using var sseDoc = JsonDocument.Parse(data);

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
    /// Fetch a web page and return plain text content (same as P1 webfetch).
    /// </summary>
    async Task<string> CallWebFetchAsync(HttpClient httpClient, BinaryData arguments, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(arguments.ToString());
        var url = doc.RootElement.GetProperty("url").GetString() ?? "";

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            return "Error: URL must start with http:// or https://";

        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Check size limit (5MB)
        if (response.Content.Headers.ContentLength > 5 * 1024 * 1024)
            return "Error: Response too large (exceeds 5MB)";

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        if (html.Length > 5 * 1024 * 1024)
            return "Error: Response too large (exceeds 5MB)";

        // Strip HTML to plain text
        var text = StripHtml(html);

        // Truncate to prevent token explosion
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

    ResearchResult? ParseResearchResult(string raw)
    {
        // Strip markdown fences if present
        var json = raw.Trim();

        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');

            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];

            if (json.EndsWith("```"))
                json = json[..^3];

            json = json.Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<ResearchResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse research result JSON");
            return null;
        }
    }
}
