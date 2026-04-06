using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

using System.Reflection;
using System.Text.Json;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Analyzes a product image to extract Visual DNA using OpenAI vision.
    /// </summary>
    /// <param name="imageBytes">Raw image bytes (PNG, JPEG, WebP, GIF).</param>
    /// <param name="mimeType">MIME type of the image (e.g., "image/jpeg").</param>
    /// <param name="model">Vision-capable model name.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after the API call completes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed <see cref="VisualDnaResult"/>, or <see langword="null"/> if parsing or validation fails.</returns>
    public virtual async Task<VisualDnaResult?> AnalyzeImageAsync(
        BinaryData imageBytes,
        string mimeType,
        string model = "gpt-5.4",
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

#pragma warning disable OPENAI001
        var chatClient = GetChatClient(model);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 2048,
            Temperature = 0.1f
        };
        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(visualDnaSystemPrompt.Value),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateImagePart(imageBytes, mimeType),
                ChatMessageContentPart.CreateTextPart("Extract Visual DNA from this product image."))
        };
#pragma warning restore OPENAI001

        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

        if (onUsage is not null && result.Value.Usage is { } usage)
        {
            onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "vision"));
        }

        var raw = result.Value.Content.FirstOrDefault()?.Text;

        if (raw is null)
            return null;

        var json = StripMarkdownFences(raw);

        try
        {
            var parsed = JsonSerializer.Deserialize<VisualDnaResult>(json, visualDnaJsonOptions);

            if (parsed is null
                || parsed.DominantColors is null
                || parsed.Mood is null
                || parsed.Materials is null
                || parsed.Style is null
                || parsed.BackgroundType is null
                || parsed.RawDescription is null)
            {
                logger.LogWarning("Visual DNA JSON missing required fields: {Response}", json.Length > 200 ? json[..200] : json);

                return null;
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Visual DNA JSON: {Response}", json.Length > 200 ? json[..200] : json);

            return null;
        }
    }

    static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();

        if (!trimmed.StartsWith("```"))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```");

        if (firstNewline > 0 && lastFence > firstNewline)
            return trimmed[(firstNewline + 1)..lastFence].Trim();

        return trimmed;
    }

    static readonly JsonSerializerOptions visualDnaJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<string> visualDnaSystemPrompt = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("ShareInvest.Agency.Prompts.visual-dna-system.md")
            ?? throw new InvalidOperationException("Embedded resource 'Prompts/visual-dna-system.md' not found.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });
}
