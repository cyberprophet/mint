using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Conducts multi-step web research on a product and returns structured market insights.
    /// Uses <see cref="WebTools"/> for provider-agnostic web search and URL fetching,
    /// driven by an OpenAI tool-calling loop.
    /// </summary>
    /// <param name="systemPrompt">System prompt that defines the research methodology and output format.</param>
    /// <param name="productInfo">Product name or description to research.</param>
    /// <param name="urls">Reference URLs to fetch and analyze (product pages, brand sites, etc.).</param>
    /// <param name="category">Optional product category hint to guide research focus.</param>
    /// <param name="model">Chat model to use for the research agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each tool-calling round.</param>
    /// <returns>Parsed <see cref="ResearchResult"/>, or <see langword="null"/> if the agent did not return valid JSON.</returns>
    public virtual async Task<ResearchResult?> ResearchProductAsync(
        string systemPrompt,
        string productInfo,
        string[] urls,
        string? category,
        string model = "gpt-5.4-nano",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

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

        var userContent = new StringBuilder($"Research this product: {PromptSanitizer.EscapeForPrompt(productInfo)}");

        if (!string.IsNullOrEmpty(category))
            userContent.Append($"\nProduct category: {PromptSanitizer.EscapeForPrompt(category)}");

        if (urls.Length > 0)
        {
            userContent.Append("\n\nReference URLs to analyze:");

            for (int i = 0; i < urls.Length; i++)
                userContent.Append($"\n{i + 1}. {urls[i]}");
        }

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent.ToString())
        };

        const int maxIterations = 10;
        var fetchedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int consecutiveFetchFailures = 0;
        int totalFetchRetries = 0;

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

            // Fetch failure budget: after 2 consecutive failures, force synthesis
            if (consecutiveFetchFailures >= 2)
            {
                logger.LogWarning("Fetch failure budget exhausted ({Count} consecutive). Injecting synthesis prompt", consecutiveFetchFailures);
                messages.Add(ChatMessage.CreateUserMessage(
                    "URL fetching is failing repeatedly. Stop trying to fetch URLs. " +
                    "Produce your final ResearchResult JSON NOW using only the search results you already have."));
                consecutiveFetchFailures = 0; // reset so the model gets one more chance to produce JSON
            }

            var iterationSw = Stopwatch.StartNew();
            var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            iterationSw.Stop();
            var completion = result.Value;

            if (onUsage is not null && completion.Usage is { } usage)
            {
                onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "research",
                    LatencyMs: (int)iterationSw.ElapsedMilliseconds, RetryCount: totalFetchRetries));
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
                                consecutiveFetchFailures = 0; // search success resets budget
                                break;

                            case "web_fetch":
                                var fetchUrl = args.RootElement.GetProperty("url").GetString() ?? "";

                                // Per-URL retry limit: skip URLs that already failed
                                if (failedUrls.Contains(fetchUrl))
                                {
                                    toolResult2 = $"[Skipped — this URL already failed. Use search results instead.]";
                                    break;
                                }

                                if (!fetchedUrls.Add(fetchUrl))
                                {
                                    toolResult2 = "[Already fetched — see previous results above]";
                                    break;
                                }
                                var fetchResult = await webTools.FetchAsync(fetchUrl, cancellationToken);
                                toolResult2 = fetchResult.ToPromptText();
                                consecutiveFetchFailures = 0; // fetch success resets budget
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

                        // Track fetch failures for budget and per-URL retry limit
                        if (toolCall.FunctionName == "web_fetch")
                        {
                            consecutiveFetchFailures++;
                            totalFetchRetries++;

                            try
                            {
                                using var failArgs = JsonDocument.Parse(toolCall.FunctionArguments.ToString());
                                var failUrl = failArgs.RootElement.GetProperty("url").GetString();
                                if (failUrl is not null) failedUrls.Add(failUrl);
                            }
                            catch { /* best-effort URL tracking */ }
                        }
                    }

                    messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, toolResult));
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var raw = completion.Content.FirstOrDefault()?.Text;

                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                return ParseResearchResult(raw, urlsWereFetched: fetchedUrls.Count > 0);
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        // Loop exhausted: attempt one final synthesis call with tools disabled
        logger.LogWarning("Research loop exhausted {Max} iterations. Attempting final synthesis", maxIterations);
        return await SynthesizePartialResultAsync(chatClient, messages, model, onUsage, cancellationToken);
    }

    async Task<ResearchResult?> SynthesizePartialResultAsync(
        ChatClient chatClient,
        List<ChatMessage> messages,
        string model,
        Action<ApiUsageEvent>? onUsage,
        CancellationToken cancellationToken)
    {
        // One final call with no tools — force the model to produce JSON from gathered data
        var synthesisOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 4096,
            Temperature = 0.1f
            // No tools added — forces a Stop finish reason
        };

        messages.Add(ChatMessage.CreateUserMessage(
            "You have gathered enough data. Produce your final ResearchResult JSON NOW. " +
            "Use only the search and fetch results from this conversation. Do not call any tools."));

        try
        {
            var result = await chatClient.CompleteChatAsync(messages, synthesisOptions, cancellationToken);
            var completion = result.Value;

            if (onUsage is not null && completion.Usage is { } usage)
                onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "research"));

            var raw = completion.Content.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(raw))
            {
                logger.LogWarning("Synthesis call returned empty response");
                return null;
            }

            var parsed = ParseResearchResult(raw, urlsWereFetched: false);

            if (parsed is not null)
                logger.LogInformation("Partial research result synthesized from exhausted loop");

            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Synthesis call failed after loop exhaustion");
            return null;
        }
    }

    internal ResearchResult? ParseResearchResult(string raw, bool urlsWereFetched = true)
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
            var result = JsonSerializer.Deserialize<ResearchResult>(json, CaseInsensitiveOptions);

            if (result is null)
                return null;

            // Backward compat: SchemaVersion 0 means the field was absent (v1 response)
            // Infer Basis from context if not provided by the model
            var basis = result.Basis;
            if (basis is null)
                basis = urlsWereFetched ? "research" : "category_inference";

            // Return a normalized record with at least schemaVersion=2 defaults
            if (result.SchemaVersion == 0 || result.Basis is null)
            {
                result = result with
                {
                    SchemaVersion = result.SchemaVersion == 0 ? 2 : result.SchemaVersion,
                    Basis = basis
                };
            }

            return result;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse research result JSON");
            return null;
        }
    }
}
