using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Maximum number of HTML characters sent to the model.
    /// Keeps token usage manageable while preserving enough copy and structure.
    /// </summary>
    const int ReferenceLinkHtmlMaxChars = 12_000;

    /// <summary>
    /// Analyzes a reference web page to extract design and messaging inspiration.
    /// </summary>
    /// <param name="url">Canonical URL of the reference page (included in the prompt for context).</param>
    /// <param name="html">Pre-fetched raw HTML of the page (scripts and styles are stripped internally).</param>
    /// <param name="context">Contextual hints: target language, optional product name.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each API call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Parsed <see cref="ReferenceLinkAnalysis"/>, or <see langword="null"/> if the model did not return
    /// valid structured output after <c>3</c> attempts.
    /// </returns>
    public virtual async Task<ReferenceLinkAnalysis?> AnalyzeReferenceLinkAsync(
        string url,
        string html,
        ReferenceLinkContext context,
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentNullException.ThrowIfNull(context);

        const string model = "gpt-5.4";

        var chatClient = GetChatClient(model);

        var generateTool = ChatTool.CreateFunctionTool(
            "generate_reference_link_analysis",
            "Saves the structured analysis of a reference landing page for design and messaging inspiration.",
            BinaryData.FromString(JsonSerializer.Serialize(ReferenceLinkToolSchema)));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 2048,
            Temperature = 0.2f
        };
        options.Tools.Add(generateTool);

        var systemPrompt = BuildReferenceLinkSystemPrompt();
        var userContent = BuildReferenceLinkUserMessage(url, html, context);

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent)
        };

        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            var result = await chatClient.CompleteChatAsync(messages, options, ct);
            sw.Stop();

            var completion = result.Value;

            if (onUsage is not null && completion.Usage is { } usage)
            {
                onUsage(new ApiUsageEvent(
                    "openai", model,
                    usage.InputTokenCount, usage.OutputTokenCount,
                    "reference_link",
                    LatencyMs: (int)sw.ElapsedMilliseconds));
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    if (toolCall.FunctionName != "generate_reference_link_analysis")
                    {
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                            $"Unknown tool: {toolCall.FunctionName}"));
                        continue;
                    }

                    try
                    {
                        var analysis = JsonSerializer.Deserialize<ReferenceLinkAnalysis>(
                            toolCall.FunctionArguments.ToString(), CaseInsensitiveOptions);

                        var validationError = ValidateReferenceLinkAnalysis(analysis);

                        if (validationError is not null)
                        {
                            logger.LogWarning(
                                "ReferenceLinkAnalysis validation failed (attempt {Attempt}): {Error}",
                                attempt + 1, validationError);
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, validationError));
                            continue;
                        }

                        return analysis;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to parse generate_reference_link_analysis arguments (attempt {Attempt})",
                            attempt + 1);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                            $"[Parse Error] Invalid JSON: {ex.Message}. Rewrite the analysis as valid JSON."));
                    }
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var text = completion.Content.FirstOrDefault()?.Text;
                logger.LogWarning(
                    "Model returned text instead of calling generate_reference_link_analysis (attempt {Attempt})",
                    attempt + 1);
                messages.Add(ChatMessage.CreateAssistantMessage(text ?? string.Empty));
                messages.Add(ChatMessage.CreateUserMessage(
                    "You must call the generate_reference_link_analysis tool with the analysis JSON. Do not return plain text."));
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        logger.LogWarning("ReferenceLinkAnalysis exhausted {Max} retries for URL: {Url}", maxRetries, url);
        return null;
    }

    // ─── Prompt builders ──────────────────────────────────────────────────────

    static string BuildReferenceLinkSystemPrompt() =>
        """
        You are a landing-page design analyst. You will be given the HTML of a reference web page.
        Your task is to extract structured design and messaging intelligence from it.

        IMPORTANT: This is a REFERENCE page for style/design inspiration — extract what makes it effective,
        not what the product is. Focus on layout patterns, visual hierarchy, tone, and persuasion technique.

        Field guidance:
        - layoutPattern: Describe the top-level content flow as a hyphenated pattern
          (e.g., "hero-problem-solution-cta", "grid-of-features", "testimonial-led-authority").
        - copyTone: Single hyphenated label for the emotional/stylistic register of the copy
          (e.g., "warm-editorial", "aggressive-sales", "minimal-premium", "playful-direct").
        - colorPalette: Up to 6 dominant hex color codes inferred from class names, inline styles,
          or OG image description. Use "#000000" if no colors can be determined.
        - typographyStyle: Character of the type treatment
          (e.g., "serif-heavy-editorial", "sans-minimal", "mixed-modern", "display-bold").
        - messagingAngles: Exactly 3 short value propositions or selling points from the page copy.
          Write each as a standalone sentence or phrase.
        - rawSummary: 2–3 sentences describing the page and what makes it an effective reference.

        Always call the generate_reference_link_analysis tool. Never return plain text.
        """;

    static string BuildReferenceLinkUserMessage(string url, string html, ReferenceLinkContext context)
    {
        var stripped = StripHtmlNoise(html);
        var truncated = stripped.Length > ReferenceLinkHtmlMaxChars
            ? stripped[..ReferenceLinkHtmlMaxChars] + "\n<!-- [truncated] -->"
            : stripped;

        var sb = new StringBuilder();
        sb.AppendLine($"Reference URL: {url}");
        sb.AppendLine($"Target language: {context.TargetLanguage}");

        if (!string.IsNullOrWhiteSpace(context.ProductName))
            sb.AppendLine($"Our product (for context only — do not conflate with the reference): {context.ProductName}");

        sb.AppendLine();
        sb.AppendLine("## Page HTML");
        sb.AppendLine(truncated);

        return sb.ToString();
    }

    // ─── Validation ────────────────────────────────────────────────────────────

    internal static string? ValidateReferenceLinkAnalysis(ReferenceLinkAnalysis? analysis)
    {
        if (analysis is null)
            return "[Validation Error] analysis object is null.";

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(analysis.LayoutPattern))
            errors.Add("[Validation Error] layoutPattern is required.");

        if (string.IsNullOrWhiteSpace(analysis.CopyTone))
            errors.Add("[Validation Error] copyTone is required.");

        if (analysis.ColorPalette is null || analysis.ColorPalette.Length == 0)
            errors.Add("[Validation Error] colorPalette must contain at least 1 hex color.");
        else if (analysis.ColorPalette.Length > 6)
            errors.Add("[Validation Error] colorPalette must not exceed 6 entries.");

        if (string.IsNullOrWhiteSpace(analysis.TypographyStyle))
            errors.Add("[Validation Error] typographyStyle is required.");

        if (analysis.MessagingAngles is null || analysis.MessagingAngles.Length == 0)
            errors.Add("[Validation Error] messagingAngles must contain at least 1 entry.");
        else if (analysis.MessagingAngles.Length > 3)
            errors.Add("[Validation Error] messagingAngles must not exceed 3 entries.");

        if (string.IsNullOrWhiteSpace(analysis.RawSummary))
            errors.Add("[Validation Error] rawSummary is required.");

        return errors.Count > 0 ? string.Join("\n", errors) : null;
    }

    // ─── HTML noise stripping ──────────────────────────────────────────────────

    /// <summary>
    /// Strips &lt;script&gt;, &lt;style&gt;, and HTML comments from raw HTML
    /// to reduce token count before sending to the model.
    /// </summary>
    internal static string StripHtmlNoise(string html)
    {
        // Remove <script>...</script> blocks (including multiline)
        var noScript = ScriptTagRegex().Replace(html, string.Empty);

        // Remove <style>...</style> blocks
        var noStyle = StyleTagRegex().Replace(noScript, string.Empty);

        // Remove HTML comments <!-- ... -->
        var noComments = HtmlCommentRegex().Replace(noStyle, string.Empty);

        // Collapse excess whitespace to single newlines for readability
        var collapsed = MultipleBlankLinesRegex().Replace(noComments, "\n\n");

        return collapsed.Trim();
    }

    [GeneratedRegex(@"<script\b[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<style\b[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex MultipleBlankLinesRegex();

    // ─── Tool schema ───────────────────────────────────────────────────────────

    static readonly object ReferenceLinkToolSchema = new
    {
        type = "object",
        properties = new
        {
            layoutPattern = new
            {
                type = "string",
                description = "Top-level content flow as a hyphenated pattern (e.g., \"hero-problem-solution-cta\")"
            },
            copyTone = new
            {
                type = "string",
                description = "Emotional/stylistic register of the copy (e.g., \"warm-editorial\", \"minimal-premium\")"
            },
            colorPalette = new
            {
                type = "array",
                items = new { type = "string" },
                minItems = 1,
                maxItems = 6,
                description = "Dominant hex color codes (up to 6) inferred from the page"
            },
            typographyStyle = new
            {
                type = "string",
                description = "Character of the type treatment (e.g., \"serif-heavy-editorial\", \"sans-minimal\")"
            },
            messagingAngles = new
            {
                type = "array",
                items = new { type = "string" },
                minItems = 1,
                maxItems = 3,
                description = "Top 3 value propositions or selling points from the page copy"
            },
            rawSummary = new
            {
                type = "string",
                description = "2–3 sentence description of the reference page and what makes it effective"
            }
        },
        required = new[] { "layoutPattern", "copyTone", "colorPalette", "typographyStyle", "messagingAngles", "rawSummary" }
    };
}
