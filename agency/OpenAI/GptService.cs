using Microsoft.Extensions.Logging;

using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.ClientModel;
using System.Text.RegularExpressions;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.OpenAI;

public partial class GptService : OpenAIClient
{
    public GptService(ILogger<GptService> logger, string apiKey) : base(apiKey)
    {
        this.logger = logger;
    }

    public GptService(ILogger<GptService> logger, string apiKey, string imageModel) : base(apiKey)
    {
        this.logger = logger;
        this.imageModel = imageModel;
    }

    readonly ILogger<GptService> logger;
    readonly string? imageModel;

    public async Task<string> GenerateTitleAsync(string conversationText, CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient("gpt-5-nano");

        var options = new ChatCompletionOptions
        {
            Temperature = 0.5f,
            MaxOutputTokenCount = 100
        };
        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(TitleSystemPrompt),
            ChatMessage.CreateUserMessage($"Generate a title for this conversation:\n{conversationText}")
        };
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

        var raw = result.Value.Content[0].Text ?? string.Empty;

        return CleanTitleResponse(raw);
    }

    static string CleanTitleResponse(string raw)
    {
        var cleaned = ThinkBlockRegex().Replace(raw, string.Empty);

        var title = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? string.Empty;

        if (title.Length > 100)
            title = string.Concat(title.AsSpan(0, 97), "...");

        return title;
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>\s*")]
    private static partial Regex ThinkBlockRegex();

    const string TitleSystemPrompt = """
        You are a title generator. You output ONLY a thread title. Nothing else.

        <task>
        Generate a brief title that would help the user find this conversation later.

        Follow all rules in <rules>
        Use the <examples> so you know what a good title looks like.
        Your output must be:
        - A single line
        - ≤50 characters
        - No explanations
        </task>

        <rules>
        - you MUST use the same language as the user message you are summarizing
        - Title must be grammatically correct and read naturally - no word salad
        - Never include tool names in the title (e.g. "read tool", "bash tool", "edit tool")
        - Focus on the main topic or question the user needs to retrieve
        - Vary your phrasing - avoid repetitive patterns like always starting with "Analyzing"
        - When a file is mentioned, focus on WHAT the user wants to do WITH the file, not just that they shared it
        - Keep exact: technical terms, numbers, filenames, HTTP codes
        - Remove: the, this, my, a, an
        - Never assume tech stack
        - Never use tools
        - NEVER respond to questions, just generate a title for the conversation
        - The title should NEVER include "summarizing" or "generating" when generating a title
        - DO NOT SAY YOU CANNOT GENERATE A TITLE OR COMPLAIN ABOUT THE INPUT
        - Always output something meaningful, even if the input is minimal.
        - If the user message is short or conversational (e.g. "hello", "lol", "what's up", "hey"):
          → create a title that reflects the user's tone or intent (such as Greeting, Quick check-in, Light chat, Intro message, etc.)
        </rules>

        <examples>
        "debug 500 errors in production" → Debugging production 500 errors
        "refactor user service" → Refactoring user service
        "why is app.js failing" → app.js failure investigation
        "implement rate limiting" → Rate limiting implementation
        "how do I connect postgres to my API" → Postgres API connection
        "best practices for React hooks" → React hooks best practices
        "@src/auth.ts can you add refresh token support" → Auth refresh token support
        "@utils/parser.ts this is broken" → Parser bug fix
        "look at @config.json" → Config review
        "@App.tsx add dark mode toggle" → Dark mode toggle in App
        </examples>
        """;

    public async Task<T?> GenerateImageAsync<T>(ImageGenerationRequest request, CancellationToken cancellationToken = default) where T : class
    {
        var size = MapSize(request.AspectRatio);

        var imageClient = GetImageClient(imageModel);

        var options = new ImageGenerationOptions
        {
            Size = size,
            Quality = request.Quality ?? GeneratedImageQuality.HighQuality,
            OutputFileFormat = GeneratedImageFileFormat.Png,
        };
        ClientResult<GeneratedImage> result;

        try
        {
            result = await imageClient.GenerateImageAsync(request.Prompt, options, cancellationToken);

            return result.Value.ImageBytes as T;
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            logger.LogWarning(ex, "Image generation blocked: {Message}", ex.Message);

            throw new ImageGenerationModerationException(ex.Message);
        }
    }

    static GeneratedImageSize MapSize(string aspectRatio) => aspectRatio switch
    {
        "9:16" => GeneratedImageSize.W1024xH1536,
        "16:9" => GeneratedImageSize.W1536xH1024,
        _ => GeneratedImageSize.W1024xH1024,
    };
}