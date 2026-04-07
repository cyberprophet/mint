using Microsoft.Extensions.Logging;

using OpenAI;
using OpenAI.Chat;

using ShareInvest.Agency.Models;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ShareInvest.Agency.OpenAI;

/// <summary>
/// Partial class inheriting <see cref="OpenAIClient"/> that provides GPT-based AI services, including title generation and image generation.
/// </summary>
public partial class GptService : OpenAIClient
{
    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger and API key.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="apiKey">OpenAI API key used to authenticate requests.</param>
    public GptService(ILogger<GptService> logger, string apiKey) : base(apiKey)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger, API key, and custom image model name.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="apiKey">OpenAI API key used to authenticate requests.</param>
    /// <param name="imageModel">Name of the OpenAI image model to use for image generation.</param>
    public GptService(ILogger<GptService> logger, string apiKey, string imageModel) : base(apiKey)
    {
        this.logger = logger;
        this.imageModel = imageModel;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GptService"/> with the specified logger, API key,
    /// custom image model name, and optional Exa API key for web research.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="apiKey">OpenAI API key used to authenticate requests.</param>
    /// <param name="imageModel">Name of the OpenAI image model to use for image generation.</param>
    /// <param name="exaApiKey">Optional Exa API key for authenticated web search.</param>
    public GptService(ILogger<GptService> logger, string apiKey, string imageModel, string? exaApiKey) : base(apiKey)
    {
        this.logger = logger;
        this.imageModel = imageModel;

        webTools = new WebTools(exaApiKey);
    }

    readonly ILogger<GptService> logger;
    readonly string? imageModel;
    readonly WebTools webTools = new();

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
            onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "title", LatencyMs: (int)sw.ElapsedMilliseconds));
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
            onUsage(new ApiUsageEvent("openai", model, repairUsage.InputTokenCount, repairUsage.OutputTokenCount,
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

}