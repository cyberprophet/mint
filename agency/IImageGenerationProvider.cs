using ShareInvest.Agency.Models;

namespace ShareInvest.Agency;

/// <summary>
/// Abstraction for AI image generation capabilities.
/// </summary>
public interface IImageGenerationProvider
{
    /// <summary>Provider identifier for telemetry.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates a single image from a text prompt.
    /// </summary>
    /// <typeparam name="T">Return type — <see cref="Uri"/> for URL, <see cref="BinaryData"/> for bytes.</typeparam>
    Task<T?> GenerateImageAsync<T>(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null) where T : class;

    /// <summary>
    /// Generates a batch of product photography shots from a source image (StudioMint pipeline).
    /// </summary>
    Task<StudioMintResult> GenerateStudioMintAsync(
        string basePrompt,
        StudioMintRequest request,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);
}
