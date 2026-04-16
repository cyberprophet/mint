namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured analysis of a reference web page for design and copy inspiration.
/// </summary>
/// <param name="LayoutPattern">
/// High-level layout pattern of the page (e.g., "hero-problem-solution-cta", "grid-of-features").
/// </param>
/// <param name="CopyTone">
/// Tone of the page copy (e.g., "warm-editorial", "aggressive-sales", "minimal-premium").
/// </param>
/// <param name="ColorPalette">
/// Dominant hex colors extracted from the page's visual style, up to 6 values.
/// </param>
/// <param name="TypographyStyle">
/// Typography character of the page (e.g., "serif-heavy-editorial", "sans-minimal", "mixed-modern").
/// </param>
/// <param name="MessagingAngles">
/// Top value propositions or selling points found in the page copy, up to 3 items.
/// </param>
/// <param name="RawSummary">
/// 2–3 sentence description of the reference page and what makes it effective.
/// </param>
public record ReferenceLinkAnalysis(
    string LayoutPattern,
    string CopyTone,
    string[] ColorPalette,
    string TypographyStyle,
    string[] MessagingAngles,
    string RawSummary);
