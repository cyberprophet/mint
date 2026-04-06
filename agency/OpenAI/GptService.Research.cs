using System.Reflection;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

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
    /// Uses <see cref="WebTools"/> for provider-agnostic web search and URL fetching,
    /// driven by an OpenAI tool-calling loop.
    /// </summary>
    /// <param name="productInfo">Product name or description to research.</param>
    /// <param name="urls">Reference URLs to fetch and analyze (product pages, brand sites, etc.).</param>
    /// <param name="category">Optional product category hint to guide research focus.</param>
    /// <param name="model">Chat model to use for the research agent.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each tool-calling round.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed <see cref="ResearchResult"/>, or <see langword="null"/> if the agent did not return valid JSON.</returns>
    public virtual async Task<ResearchResult?> ResearchProductAsync(
        string productInfo,
        string[] urls,
        string? category,
        string model = "gpt-5.4-nano",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        var chatClient = GetChatClient(model);

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

        const int maxIterations = 10;
        var fetchedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < maxIterations; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Sliding window: keep the last keepCount tool messages intact;
            // replace all older tool messages with brief content-aware summaries
            // so the model knows what was already retrieved without re-querying.
            const int keepCount = 4;
            int toolMessageCount = messages.Count(m => m is ToolChatMessage);
            if (toolMessageCount > keepCount)
            {
                int toCompress = toolMessageCount - keepCount;
                int compressed = 0;
                for (int m = 2; m < messages.Count && compressed < toCompress; m++)
                {
                    if (messages[m] is ToolChatMessage tcm)
                    {
                        var original = tcm.Content.FirstOrDefault()?.Text ?? string.Empty;
                        var snippet = original.Length <= 150
                            ? original
                            : original[..150];
                        // Trim to the last word boundary to avoid mid-word cuts.
                        var lastSpace = snippet.LastIndexOf(' ');
                        if (lastSpace > 80)
                            snippet = snippet[..lastSpace];
                        var summary = $"[Compressed] {snippet}...";
                        messages[m] = ChatMessage.CreateToolMessage(tcm.ToolCallId, summary);
                        compressed++;
                    }
                }
            }

            var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = result.Value;

            if (onUsage is not null && completion.Usage is { } usage)
            {
                onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "research"));
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    string toolResult;

                    try
                    {
                        using var args = JsonDocument.Parse(toolCall.FunctionArguments.ToString());

                        string toolResult2;

                        switch (toolCall.FunctionName)
                        {
                            case "web_search_exa":
                                toolResult2 = await webTools.SearchAsync(
                                    args.RootElement.GetProperty("query").GetString() ?? "",
                                    args.RootElement.TryGetProperty("numResults", out var nr) ? nr.GetInt32() : 8,
                                    cancellationToken);
                                break;

                            case "web_fetch":
                                var fetchUrl = args.RootElement.GetProperty("url").GetString() ?? "";
                                if (!fetchedUrls.Add(fetchUrl))
                                {
                                    toolResult2 = "[Already fetched — see previous results above]";
                                    break;
                                }
                                toolResult2 = await webTools.FetchAsync(fetchUrl, cancellationToken);
                                break;

                            default:
                                toolResult2 = $"Unknown tool: {toolCall.FunctionName}";
                                break;
                        }

                        toolResult = toolResult2;
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

    ResearchResult? ParseResearchResult(string raw)
    {
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
            return JsonSerializer.Deserialize<ResearchResult>(json, CaseInsensitiveOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse research result JSON");
            return null;
        }
    }
}
