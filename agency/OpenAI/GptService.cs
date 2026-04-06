using Microsoft.Extensions.Logging;

using OpenAI;
using OpenAI.Chat;

using System.Reflection;
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
        this.webTools = new WebTools(exaApiKey);
    }

    readonly ILogger<GptService> logger;
    readonly string? imageModel;
    readonly WebTools webTools = new();

    /// <summary>
    /// Generates a short title (50 characters or fewer) summarising the given conversation text using gpt-5-nano.
    /// </summary>
    /// <param name="conversationText">The full conversation text to summarise as a title.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A trimmed title string, or <see langword="null"/> if no usable content was returned.</returns>
    public virtual async Task<string?> GenerateTitleAsync(string conversationText, CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient("gpt-5-nano");

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 1024
        };
        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(titleSystemPrompt.Value),
            ChatMessage.CreateUserMessage($"<conversation>\n{conversationText}\n</conversation>")
        };
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

        var raw = result.Value.Content.FirstOrDefault()?.Text;

        return raw is null ? null : CleanTitleResponse(raw);
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

    static readonly Lazy<string> titleSystemPrompt = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ShareInvest.Agency.Prompts.title-system.md")
            ?? throw new InvalidOperationException("Embedded resource 'Prompts/title-system.md' not found.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });
}