using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

using System.Diagnostics;

using System.Text.Json;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Analyzes a product image to extract Visual DNA using OpenAI vision.
    /// </summary>
    /// <param name="systemPrompt">System prompt that defines the visual analysis rules and output format.</param>
    /// <param name="imageBytes">Raw image bytes (PNG, JPEG, WebP, GIF).</param>
    /// <param name="mimeType">MIME type of the image (e.g., "image/jpeg").</param>
    /// <param name="model">Vision-capable model name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after the API call completes.</param>
    /// <returns>Parsed <see cref="VisualDnaResult"/>, or <see langword="null"/> if parsing or validation fails.</returns>
    public virtual async Task<VisualDnaResult?> AnalyzeImageAsync(
        string systemPrompt,
        BinaryData imageBytes,
        string mimeType,
        string model = "gpt-5.4",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
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
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateImagePart(imageBytes, mimeType),
                ChatMessageContentPart.CreateTextPart("Extract Visual DNA from this product image."))
        };
#pragma warning restore OPENAI001

        var sw = Stopwatch.StartNew();
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        sw.Stop();

        if (onUsage is not null && result.Value.Usage is { } usage)
        {
            onUsage(new ApiUsageEvent(ProviderName, model, usage.InputTokenCount, usage.OutputTokenCount, "vision", LatencyMs: (int)sw.ElapsedMilliseconds));
        }

        var raw = result.Value.Content.FirstOrDefault()?.Text;

        if (raw is null)
            return null;

        var parsed = TryParseVisualDna(raw);

        if (parsed is not null)
            return parsed.Normalize();

        // Repair attempt: send a correction prompt if original response was non-empty
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        logger.LogWarning("Visual DNA JSON parse failed — attempting repair prompt. Original (first 200): {Snippet}",
            raw.Length > 200 ? raw[..200] : raw);

        var repairMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateImagePart(imageBytes, mimeType),
                ChatMessageContentPart.CreateTextPart("Extract Visual DNA from this product image.")),
            ChatMessage.CreateAssistantMessage(raw),
            ChatMessage.CreateUserMessage(
                "Your response was not valid JSON. Return ONLY a JSON object with these exact fields: " +
                "dominantColors, mood, materials, style, backgroundType, rawDescription")
        };

        var repairSw = Stopwatch.StartNew();
        var repairResult = await chatClient.CompleteChatAsync(repairMessages, options, cancellationToken);
        repairSw.Stop();

        if (onUsage is not null && repairResult.Value.Usage is { } repairUsage)
        {
            onUsage(new ApiUsageEvent(ProviderName, model, repairUsage.InputTokenCount, repairUsage.OutputTokenCount,
                "vision", LatencyMs: (int)repairSw.ElapsedMilliseconds));
        }

        var repairRaw = repairResult.Value.Content.FirstOrDefault()?.Text;

        if (repairRaw is null)
            return null;

        var repaired = TryParseVisualDna(repairRaw);

        if (repaired is null)
            logger.LogWarning("Visual DNA repair attempt also failed");

        return repaired?.Normalize();
    }

    VisualDnaResult? TryParseVisualDna(string raw)
    {
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

}
