using Microsoft.Extensions.Logging;

using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.ClientModel;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Generates an image via the OpenAI Images API based on the provided request and returns the result as <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to cast the image bytes to (e.g., <see cref="BinaryData"/>).</typeparam>
    /// <param name="request">Parameters describing the image to generate, including prompt, aspect ratio, and quality.</param>
    /// <param name="onUsage">Optional callback invoked after successful image generation for audit purposes (token counts are 0 as the image API does not report token usage).</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The generated image bytes cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast fails.</returns>
    /// <exception cref="ImageGenerationModerationException">Thrown when OpenAI's safety system rejects the request.</exception>
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
        ClientResult<GeneratedImage> result;

        try
        {
            result = await imageClient.GenerateImageAsync(request.Prompt, options, cancellationToken);

            if (onUsage is not null)
            {
                onUsage(new ApiUsageEvent("openai", imageModel ?? "gpt-image-1", 0, 0, "image"));
            }

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