using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

using System.Reflection;
using System.Text.Json;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Analyzes a product image to extract Visual DNA using OpenAI vision.
    /// </summary>
    /// <param name="imageBytes">Raw image bytes (PNG, JPEG, WebP, GIF).</param>
    /// <param name="mimeType">MIME type of the image (e.g., "image/jpeg").</param>
    /// <param name="model">Vision-capable model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed <see cref="VisualDnaResult"/>, or <see langword="null"/> if parsing fails.</returns>
    public virtual async Task<VisualDnaResult?> AnalyzeImageAsync(
        BinaryData imageBytes,
        string mimeType,
        string model = "gpt-5.4",
        CancellationToken cancellationToken = default)
    {
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
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);

        var raw = result.Value.Content.FirstOrDefault()?.Text;

        if (raw is null)
            return null;

        var json = StripMarkdownFences(raw);

        try
        {
            return JsonSerializer.Deserialize<VisualDnaResult>(json, visualDnaJsonOptions);
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
