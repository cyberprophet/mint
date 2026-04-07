namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured storyboard output produced by the Apollo copywriting engine.
/// </summary>
/// <param name="Sections">Ordered sections of the product detail page persuasion flow.</param>
/// <param name="CtaText">Bottom-most purchase call-to-action button or benefit copy.</param>
public record StoryboardResult(
    StoryboardSection[] Sections,
    string CtaText);

/// <summary>
/// A single section within a storyboard, representing one step in the persuasion flow.
/// </summary>
/// <param name="Title">Section name (e.g., "Hero", "Problem Awareness", "Trust Signals").</param>
/// <param name="StrategicIntent">Marketing purpose of this section in the persuasion flow.</param>
/// <param name="SectionType">Optional section type hint (e.g., "hero", "value", "cta").</param>
/// <param name="Blocks">Content blocks within this section.</param>
public record StoryboardSection(
    string Title,
    string StrategicIntent,
    string? SectionType,
    StoryboardBlock[] Blocks);

/// <summary>
/// A single content block within a storyboard section.
/// </summary>
/// <param name="Type">Block type: "heading", "text", "image", or "highlight".</param>
/// <param name="Content">Body text, subheading, or image generation prompt (English for image blocks).</param>
public record StoryboardBlock(
    string Type,
    string Content);
