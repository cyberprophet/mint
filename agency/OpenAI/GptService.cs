using Microsoft.Extensions.Logging;

using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ShareInvest.Agency.OpenAI;

/// <summary>
/// OpenAI / OpenAI-compatible provider implementing text generation, vision, and image generation.
/// Uses composition over inheritance to allow endpoint customisation for compatible providers
/// (MiniMax, Groq, Fireworks, Together AI, etc.).
/// </summary>
public partial class GptService : ITextGenerationProvider, IVisionProvider, IImageGenerationProvider
{
    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger and API key.
    /// </summary>
    public GptService(ILogger<GptService> logger, string apiKey)
    {
        client = new OpenAIClient(apiKey);
        this.logger = logger;
        webTools = new WebTools();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger, API key, and custom image model name.
    /// </summary>
    public GptService(ILogger<GptService> logger, string apiKey, string imageModel)
    {
        client = new OpenAIClient(apiKey);
        this.logger = logger;
        this.imageModel = imageModel;
        webTools = new WebTools();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger, API key,
    /// custom image model name, and optional Exa API key for web research.
    /// </summary>
    public GptService(ILogger<GptService> logger, string apiKey, string imageModel, string? exaApiKey)
    {
        client = new OpenAIClient(apiKey);
        this.logger = logger;
        this.imageModel = imageModel;
        webTools = new WebTools(exaApiKey);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with a custom endpoint for OpenAI-compatible providers.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="apiKey">Provider API key.</param>
    /// <param name="options">Client options with custom <see cref="OpenAIClientOptions.Endpoint"/>.</param>
    /// <param name="imageModel">Name of the image model to use for image generation.</param>
    /// <param name="exaApiKey">Optional Exa API key for web research.</param>
    /// <param name="providerName">Provider name for telemetry (e.g., "groq", "minimax").</param>
    public GptService(ILogger<GptService> logger, string apiKey, OpenAIClientOptions options, string? imageModel = null, string? exaApiKey = null, string providerName = "openai")
    {
        client = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
        this.logger = logger;
        this.imageModel = imageModel;
        this.providerName = providerName;
        webTools = new WebTools(exaApiKey);
    }

    /// <inheritdoc />
    public string ProviderName => providerName;

    readonly OpenAIClient client;
    readonly ILogger<GptService> logger;
    readonly string? imageModel;
    readonly string providerName = "openai";
    readonly WebTools webTools;

    /// <summary>Gets a chat client for the specified model from the composed <see cref="OpenAIClient"/>.</summary>
    internal virtual ChatClient GetChatClient(string model) => client.GetChatClient(model);

    /// <summary>Gets an image client for the specified model from the composed <see cref="OpenAIClient"/>.</summary>
    internal virtual ImageClient GetImageClient(string? model) => client.GetImageClient(model);

    /// <summary>
    /// Generates a short title (50 characters or fewer) summarising the given conversation text using gpt-5-nano.
    /// </summary>
    /// <param name="systemPrompt">System prompt that defines the title generation rules and output format.</param>
    /// <param name="conversationText">The full conversation text to summarise as a title.</param>
    /// <param name="model">Chat model to use for title generation.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after the API call completes.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A trimmed title string, or <see langword="null"/> if no usable content was returned.</returns>
    public virtual async Task<string?> GenerateTitleAsync(string systemPrompt, string conversationText, string model = "gpt-5-nano", Action<ApiUsageEvent>? onUsage = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var chatClient = GetChatClient(model);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 1024
        };
        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage($"<conversation>\n{conversationText}\n</conversation>")
        };
        var sw = Stopwatch.StartNew();
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        sw.Stop();

        if (onUsage is not null && result.Value.Usage is { } usage)
        {
            onUsage(new ApiUsageEvent(ProviderName, model, usage.InputTokenCount, usage.OutputTokenCount, "title", LatencyMs: (int)sw.ElapsedMilliseconds));
        }

        var raw = result.Value.Content.FirstOrDefault()?.Text;

        if (raw is null)
            return null;

        var title = CleanTitleResponse(raw);

        if (title is not null)
            return title;

        // Repair attempt: <think> removal yielded empty — send a correction prompt
        logger.LogWarning("Title generation returned non-title content — attempting repair prompt");

        var repairMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage($"<conversation>\n{conversationText}\n</conversation>"),
            ChatMessage.CreateAssistantMessage(raw),
            ChatMessage.CreateUserMessage(
                "Your response contained non-title content. Return ONLY the title text, no explanation.")
        };

        var repairSw = Stopwatch.StartNew();
        var repairResult = await chatClient.CompleteChatAsync(repairMessages, options, cancellationToken);
        repairSw.Stop();

        if (onUsage is not null && repairResult.Value.Usage is { } repairUsage)
        {
            onUsage(new ApiUsageEvent(ProviderName, model, repairUsage.InputTokenCount, repairUsage.OutputTokenCount,
                "title", LatencyMs: (int)repairSw.ElapsedMilliseconds));
        }

        var repairRaw = repairResult.Value.Content.FirstOrDefault()?.Text;

        if (repairRaw is null)
            return null;

        var repairedTitle = CleanTitleResponse(repairRaw);

        if (repairedTitle is null)
            logger.LogWarning("Title repair attempt also returned empty result");

        return repairedTitle;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        webTools.Dispose();
    }

    static string? CleanTitleResponse(string raw)
    {
        var cleaned = ThinkBlockRegex().Replace(raw, string.Empty);

        var title = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (title is null)
            return null;

        if (title.Length > 50)
            title = string.Concat(title.AsSpan(0, 47), "...");

        return title;
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>\s*")]
    private static partial Regex ThinkBlockRegex();

    /// <summary>
    /// Classifies validation error strings into diagnostic categories for structured logging.
    /// Used by Intent 037 Phase A to identify which validation rules fire most often.
    /// </summary>
    internal static string[] ClassifyValidationErrors(string validationError)
    {
        var categories = new List<string>();

        foreach (var line in validationError.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var category = line switch
            {
                _ when line.Contains("visualBlocks") => "visualBlocks_constraint",
                _ when line.Contains("pageDesignSystem") => "pageDesignSystem_incomplete",
                _ when line.Contains("blockType") && line.Contains("invalid") => "invalid_blockType",
                _ when line.Contains("reuses layoutVariant") => "repeated_layoutVariant",
                _ when line.Contains("layoutVariant") => "invalid_layoutVariant",
                _ when line.Contains("panel") && (line.Contains("requires at least") || line.Contains("should have at least one assetSlot per panel")) => "insufficient_panels",
                _ when line.Contains("hero") && line.Contains("heightWeight") => "hero_heightWeight",
                _ when line.Contains("CTA") || line.Contains("offer-reassurance-sticky") => "cta_heightWeight",
                _ when line.Contains("assetSlot") && line.Contains("per panel") => "slot_panel_mismatch",
                _ when line.Contains("assetSlot") => "missing_assetSlot",
                _ when line.Contains("forbidden pattern") => "forbidden_prompt_pattern",
                _ when line.Contains("prompt too short") => "prompt_too_short",
                _ when line.Contains("negativeConstraints") => "missing_negativeConstraints",
                _ when line.Contains("Rhythm Error") && line.Contains("blockType") => "rhythm_blockType",
                _ when line.Contains("Rhythm Error") && line.Contains("heightWeight") => "rhythm_heightWeight",
                _ when line.Contains("unique blockTypes") || line.Contains("block types for variety") => "low_blockType_diversity",
                _ when line.Contains("missing type: \"image\"") => "missing_image_block",
                _ when line.Contains("not primarily English") => "image_prompt_not_english",
                _ when line.Contains("too short") => "image_prompt_too_short",
                _ when line.Contains("Generic copy") || line.Contains("Forbidden cliché") => "generic_copy",
                _ when line.Contains("references external context") => "external_reference",
                _ when line.Contains("language mismatch") => "onscreen_text_language",
                _ when line.Contains("appears to be in English") => "copy_wrong_language",
                _ when line.Contains("Missing required section") => "missing_required_section",
                _ => "other"
            };

            if (!categories.Contains(category))
                categories.Add(category);
        }

        return categories.Count > 0 ? [.. categories] : ["unknown"];
    }

}