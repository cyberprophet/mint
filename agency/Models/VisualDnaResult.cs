namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured Visual DNA extracted from a product image via AI vision analysis.
/// </summary>
/// <param name="DominantColors">HEX color codes, max 6.</param>
/// <param name="Mood">Atmosphere description (e.g., "premium", "minimal", "vibrant").</param>
/// <param name="Materials">Material/texture descriptors (e.g., ["matte", "glass", "fabric"]).</param>
/// <param name="Style">Style classification (e.g., "luxury skincare", "casual fashion").</param>
/// <param name="BackgroundType">Background type (e.g., "white studio", "lifestyle", "gradient").</param>
/// <param name="RawDescription">Comprehensive visual analysis narrative, max 1500 chars.</param>
public record VisualDnaResult(
    string[] DominantColors,
    string Mood,
    string[] Materials,
    string Style,
    string BackgroundType,
    string RawDescription);
