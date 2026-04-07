using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Generates a Tailwind CSS HTML design for a product detail page using an OpenAI tool-calling loop.
    /// The system prompt is injected by the caller (not embedded) to keep business-sensitive
    /// design prompts out of the public NuGet package.
    /// </summary>
    /// <param name="systemPrompt">Full Athena system prompt assembled by the caller.</param>
    /// <param name="context">Design HTML generation context (blueprint, storyboard, optional brief/feedback).</param>
    /// <param name="model">Chat model to use for the design agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each round.</param>
    /// <returns>A <see cref="DesignHtmlResult"/> containing the sanitized HTML, or <see langword="null"/> if the agent did not return valid HTML.</returns>
    public virtual async Task<DesignHtmlResult?> GenerateDesignHtmlAsync(
        string systemPrompt,
        DesignHtmlContext context,
        string model = "gpt-5.4",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        var chatClient = GetChatClient(model);

        var renderAndPreviewTool = ChatTool.CreateFunctionTool(
            "render_and_preview_design",
            "Submit the rendered Tailwind CSS HTML design for preview. Call this when the HTML is complete.",
            BinaryData.FromString(JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    html = new
                    {
                        type = "string",
                        description = "Complete Tailwind CSS HTML code for the product detail page"
                    }
                },
                required = new[] { "html" }
            })));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 16384,
            Temperature = 0.3f
        };
        options.Tools.Add(renderAndPreviewTool);

        var userContent = JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent)
        };

        const int maxRetries = 3;
        int totalTokens = 0;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = result.Value;

            if (completion.Usage is { } usage)
            {
                totalTokens += usage.InputTokenCount + usage.OutputTokenCount;

                if (onUsage is not null)
                {
                    onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "design"));
                }
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    if (toolCall.FunctionName != "render_and_preview_design")
                    {
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, $"Unknown tool: {toolCall.FunctionName}"));
                        continue;
                    }

                    try
                    {
                        var args = JsonSerializer.Deserialize<JsonElement>(
                            toolCall.FunctionArguments.ToString(), CaseInsensitiveOptions);

                        if (!args.TryGetProperty("html", out var htmlElement))
                        {
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                                "[Validation Error] Missing required property 'html' in tool arguments."));
                            continue;
                        }

                        var html = htmlElement.GetString() ?? string.Empty;

                        // Gates 4-5: Auto-sanitize (strip <script> and on* handlers)
                        html = SanitizeHtml(html);

                        // Gates 1-3, 6: Validate
                        var expectedSectionCount = context.Blueprint?.VisualBlocks?.Length ?? 0;
                        var validationError = ValidateDesignHtml(html, expectedSectionCount);

                        if (validationError is not null)
                        {
                            logger.LogWarning("Design HTML validation failed (attempt {Attempt}): {Error}",
                                attempt + 1, validationError.Length > 300 ? validationError[..300] + "..." : validationError);
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, validationError));
                            continue;
                        }

                        return new DesignHtmlResult(html, totalTokens, attempt + 1);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to parse render_and_preview_design arguments (attempt {Attempt})", attempt + 1);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                            $"[Parse Error] Invalid JSON: {ex.Message}. Resubmit the HTML as valid JSON."));
                    }
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var text = completion.Content.FirstOrDefault()?.Text;
                logger.LogWarning("Model returned text instead of calling render_and_preview_design (attempt {Attempt})", attempt + 1);
                messages.Add(ChatMessage.CreateAssistantMessage(text ?? ""));
                messages.Add(ChatMessage.CreateUserMessage(
                    "You must call the render_and_preview_design tool with the complete HTML. Do not return plain text."));
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        logger.LogWarning("Design HTML generation exhausted {Max} retries", maxRetries);
        return null;
    }

    // ─── Design HTML Validation (Gates 1-3, 6) ────────────────────

    static string? ValidateDesignHtml(string html, int expectedSectionCount)
    {
        // Gate 1: Non-empty
        if (string.IsNullOrWhiteSpace(html))
            return "HTML content is empty. Generate the complete Tailwind CSS HTML design.";

        // Gate 2: Max size (1MB)
        if (html.Length > 1_000_000)
            return $"HTML exceeds 1,000,000 character limit (current: {html.Length:N0}). Reduce the HTML size.";

        // Gate 3: Contains <section>
        if (!html.Contains("<section", StringComparison.OrdinalIgnoreCase))
            return "HTML must contain <section> tags. Wrap each visual block in a <section> element.";

        // Gate 6: Section count >= expected
        if (expectedSectionCount > 0)
        {
            var sectionCount = Regex.Matches(html, @"<section\b", RegexOptions.IgnoreCase).Count;

            if (sectionCount < expectedSectionCount)
                return $"Expected at least {expectedSectionCount} <section> tags (one per visual block) but found {sectionCount}. Add the missing sections.";
        }

        return null; // Valid
    }

    // ─── Design HTML Sanitization (Gates 4-5) ─────────────────────

    static string SanitizeHtml(string html)
    {
        // Gate 4: Strip <script> tags
        html = Regex.Replace(html, @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>", "", RegexOptions.IgnoreCase);
        // Gate 5: Strip on* event handlers
        html = Regex.Replace(html, @"\s+on\w+\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]*)", "", RegexOptions.IgnoreCase);
        return html;
    }
}
