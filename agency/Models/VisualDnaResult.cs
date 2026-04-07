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
    string RawDescription)
{
    // ── Allowed label sets (union of prompt labels + issue #17 additions) ──────

    /// <summary>Allowed values for <see cref="Mood"/>. Values not in this set are normalized to "unknown".</summary>
    public static readonly IReadOnlySet<string> AllowedMoods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "premium", "minimal", "clinical", "playful", "vibrant", "natural", "bold", "technical", "soft",
        "casual", "warm", "cool", "energetic", "serene", "professional", "rustic", "modern",
        "unknown"
    };

    /// <summary>Allowed values for <see cref="Style"/>. Values not in this set are normalized to "unknown".</summary>
    public static readonly IReadOnlySet<string> AllowedStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "luxury-minimal", "studio-clean", "lifestyle-natural", "editorial-bold", "clinical-white", "playful-colorful",
        "minimal", "luxury", "industrial", "natural", "retro", "tech", "handcrafted", "clinical", "lifestyle", "editorial",
        "unknown"
    };

    /// <summary>Allowed values for <see cref="BackgroundType"/>. Values not in this set are normalized to "unknown".</summary>
    public static readonly IReadOnlySet<string> AllowedBackgroundTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "white-studio", "solid-color", "gradient", "flat-lay", "lifestyle-indoor", "lifestyle-outdoor",
        "transparent-cutout", "textured-surface",
        "solid", "studio", "lifestyle", "transparent", "textured", "outdoor", "abstract",
        "unknown"
    };

    /// <summary>
    /// Returns a copy of this record with <see cref="Mood"/>, <see cref="Style"/>, and
    /// <see cref="BackgroundType"/> normalized to their allowed label sets.
    /// Any value not in the allowed set is replaced with "unknown".
    /// </summary>
    public VisualDnaResult Normalize() => this with
    {
        Mood = AllowedMoods.Contains(Mood) ? Mood.ToLowerInvariant() : "unknown",
        Style = AllowedStyles.Contains(Style) ? Style.ToLowerInvariant() : "unknown",
        BackgroundType = AllowedBackgroundTypes.Contains(BackgroundType) ? BackgroundType.ToLowerInvariant() : "unknown"
    };
}
