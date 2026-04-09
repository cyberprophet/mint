namespace ShareInvest.Agency.Models;

/// <summary>
/// Input context for the storyboard generation service.
/// All fields are pre-serialized JSON strings assembled by the caller.
/// </summary>
/// <param name="Brief">JSON: targetAudience, sellingPoints, toneAndManner, colorPalette, productName, productType.</param>
/// <param name="MarketContext">JSON: categoryInsights, competitorPositioning, consumerPainPoints, buyingMotivations, differentiationAngles, primaryAngle, forbiddenCliches, narrativeBias.</param>
/// <param name="VisualDna">JSON array of Visual DNA entries (dominantColors, mood, materials, style, backgroundType, rawDescription), or null if unavailable.</param>
/// <param name="TargetLanguage">Target language code: "ko", "en", "ja", or "zh".</param>
/// <param name="ForbiddenCliches">Product-specific forbidden expressions extracted from marketContext, or null.</param>
/// <param name="ProductType">Product category (e.g., "텀블러", "노트북"). Used to determine whether spec-table is required.</param>
/// <param name="Feedback">Validation error from a previous attempt (used for retry), or null on first call.</param>
public record StoryboardContext(
    string Brief,
    string MarketContext,
    string? VisualDna,
    string TargetLanguage,
    string[]? ForbiddenCliches,
    string? ProductType,
    string? Feedback);
