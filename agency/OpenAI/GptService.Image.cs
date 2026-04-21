using Microsoft.Extensions.Logging;

using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.ClientModel;
using System.Diagnostics;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Generates an image via the OpenAI Images API based on the provided request and returns the result as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to cast the image bytes to (e.g., <see cref="BinaryData"/>).</typeparam>
    /// <param name="request">Parameters describing the image to generate, including prompt, aspect ratio, and quality.</param>
    /// <param name="onUsage">Optional callback invoked after successful image generation for audit purposes.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The generated image bytes cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast fails.</returns>
    /// <exception cref="ImageGenerationModerationException">Thrown when OpenAI's safety system rejects the request (HTTP 400).</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the API key is invalid or lacks image generation permissions (HTTP 401).</exception>
    /// <exception cref="ImageRateLimitedException">Thrown when the OpenAI rate limit is exceeded (HTTP 429).</exception>
    public async Task<T?> GenerateImageAsync<T>(ImageGenerationRequest request, CancellationToken cancellationToken = default, Action<ApiUsageEvent>? onUsage = null) where T : class
    {
        var size = MapSize(request.AspectRatio);

        var imageClient = GetImageClient(imageModel);

        var options = new ImageGenerationOptions
        {
            Size = size,
            Quality = request.Quality ?? GeneratedImageQuality.HighQuality,
            OutputFileFormat = GeneratedImageFileFormat.Png,
        };
        ClientResult<GeneratedImageCollection> result;

        try
        {
            var sw = Stopwatch.StartNew();
            result = await imageClient.GenerateImagesAsync(request.Prompt, 1, options, cancellationToken);
            sw.Stop();

            if (onUsage is not null)
            {
                var usage = result.Value.Usage;
                var actualQuality = request.Quality ?? GeneratedImageQuality.HighQuality;
                var qualityName = actualQuality == GeneratedImageQuality.LowQuality ? "low"
                    : actualQuality == GeneratedImageQuality.MediumQuality ? "medium"
                    : "high";
                var sizeName = size == GeneratedImageSize.W1024xH1536 ? "1024x1536"
                    : size == GeneratedImageSize.W1536xH1024 ? "1536x1024"
                    : "1024x1024";
                // InputTokenDetails.{TextTokenCount, ImageTokenCount} is
                // OpenAI's per-call split. Pure prompt-to-image generation
                // has ImageTokenCount == 0 (no source image sent);
                // ApiUsageEvent carries them separately so
                // ModelPricingTable can bill each bucket at its own rate.
                var textInput = (int)(usage?.InputTokenDetails?.TextTokenCount ?? usage?.InputTokenCount ?? 0);
                var imageInput = (int)(usage?.InputTokenDetails?.ImageTokenCount ?? 0);
                onUsage(new ApiUsageEvent(ProviderName, imageModel ?? "gpt-image-1",
                    textInput,
                    (int)(usage?.OutputTokenCount ?? 0),
                    "image", LatencyMs: (int)sw.ElapsedMilliseconds,
                    ImageQuality: qualityName, ImageSize: sizeName,
                    ImageInputTokens: imageInput > 0 ? imageInput : null));
            }

            return result.Value[0].ImageBytes as T;
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            logger.LogWarning(ex, "Image generation blocked by moderation: {Message}", ex.Message);

            throw new ImageGenerationModerationException(ex.Message, ex);
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            logger.LogError(ex, "Image generation authentication failed — check API key: {Message}", ex.Message);

            throw new UnauthorizedAccessException(
                "OpenAI image generation authentication failed. Verify the API key is valid and has image generation permissions.",
                ex);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            logger.LogWarning(ex, "Image generation rate limit exceeded: {Message}", ex.Message);

            throw new ImageRateLimitedException(
                "OpenAI image generation rate limit exceeded. Reduce request frequency or upgrade your usage tier.",
                ex);
        }
    }

    static GeneratedImageSize MapSize(string aspectRatio) => aspectRatio switch
    {
        "9:16" => GeneratedImageSize.W1024xH1536,
        "16:9" => GeneratedImageSize.W1536xH1024,
        _ => GeneratedImageSize.W1024xH1024,
    };
}