using Microsoft.Extensions.Logging;

using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.ClientModel;
using System.Reflection;
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

    public async Task<string?> GenerateTitleAsync(string conversationText, CancellationToken cancellationToken = default)
    {
        var chatClient = GetChatClient("gpt-5-nano");

        var options = new ChatCompletionOptions
        {
            Temperature = 0.5f,
            MaxOutputTokenCount = 100
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