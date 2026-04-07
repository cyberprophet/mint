namespace ShareInvest.Agency.Models;

/// <summary>
/// Input context for the blueprint generation service (Pygmalion).
/// All JSON fields are pre-serialized strings assembled by the caller.
/// </summary>
/// <param name="StoryboardJson">JSON: the approved storyboard (sections[] with title, strategicIntent, sectionType, blocks; ctaText).</param>
/// <param name="VisualDna">JSON array of Visual DNA entries (dominantColors, mood, materials, style, backgroundType, rawDescription), or null if unavailable.</param>
/// <param name="BriefJson">JSON: brief context (toneAndManner, colorPalette, productName, productType), or null.</param>
/// <param name="Feedback">Validation error from a previous attempt (used for retry), or null on first call.</param>
public record BlueprintContext(
    string StoryboardJson,
    string? VisualDna,
    string? BriefJson,
    string? Feedback);
