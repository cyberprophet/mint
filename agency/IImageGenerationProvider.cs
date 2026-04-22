using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

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
    /// <param name="basePrompt">Full StudioMint base prompt assembled by the caller.</param>
    /// <param name="request">The source image plus optional intent guidance.</param>
    /// <param name="cancellationToken">Cancels the entire batch.</param>
    /// <param name="onUsage">Optional usage callback — invoked once per successful shot.</param>
    /// <param name="shots">
    /// Shot definitions to generate. When <c>null</c>, the implementation falls back to its
    /// internal v1 defaults for backward compatibility. Pass an explicit list to use the
    /// rev.3 industry 4-cut pack (cutout / styled / detail / special). Placed last so
    /// existing positional callers (<c>..., ct)</c>) continue to compile after 0.16.0.
    /// </param>
    Task<StudioMintResult> GenerateStudioMintAsync(
        string basePrompt,
        StudioMintRequest request,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null,
        IReadOnlyList<StudioMintShotDefinition>? shots = null);
}
