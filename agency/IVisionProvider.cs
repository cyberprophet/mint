using ShareInvest.Agency.Models;

namespace ShareInvest.Agency;

/// <summary>
/// Abstraction for multimodal image analysis capabilities.
/// </summary>
public interface IVisionProvider
{
    /// <summary>Provider identifier for telemetry.</summary>
    string ProviderName { get; }

    /// <summary>
    /// Extracts Visual DNA from a product image (colors, mood, materials, style, background).
    /// </summary>
    Task<VisualDnaResult?> AnalyzeImageAsync(
        string systemPrompt,
        BinaryData imageBytes,
        string mimeType,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);
}
