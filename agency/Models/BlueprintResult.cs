namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured layout blueprint output produced by the Pygmalion blueprint engine.
/// Defines a page-level design system, visual block structure, panel layouts,
/// and photo-only asset slot prompts for the hybrid rendering pipeline.
/// </summary>
/// <param name="PageDesignSystem">Page-level design tokens inherited by all blocks.</param>
/// <param name="VisualBlocks">Ordered visual blocks mapping storyboard sections to layout structures.</param>
/// <param name="Assumptions">Inferences made when input was ambiguous (e.g., sectionType derived from strategicIntent).</param>
public record BlueprintResult(
    PageDesignSystem PageDesignSystem,
    VisualBlock[] VisualBlocks,
    string[]? Assumptions);

/// <summary>
/// Page-level design system — defined once at the top of the blueprint.
/// All blocks inherit these values and only override when genuinely different.
/// </summary>
/// <param name="Mood">Overall emotional atmosphere (e.g., "fresh, sporty, clean, modern").</param>
/// <param name="BrandColors">Ordered hex codes — primary first, derived from Visual DNA or brief.</param>
/// <param name="BackgroundApproach">Page-wide background strategy (e.g., "dark studio cutout with charcoal reflections").</param>
/// <param name="TypographyScale">Base typographic scale (e.g., "display-xl, body-md, highlights-sm").</param>
public record PageDesignSystem(
    string Mood,
    string[] BrandColors,
    string BackgroundApproach,
    string TypographyScale);

/// <summary>
/// A single visual block grouping one or more storyboard sections into a layout unit.
/// </summary>
/// <param name="BlockId">Unique identifier for this block within the blueprint.</param>
/// <param name="BlockType">Block type from the defined vocabulary (hero, vertical-triptych, value-benefit, comparison-split, proof-trust, benefit-grid, timeline, offer-reassurance-sticky).</param>
/// <param name="SectionRefs">Storyboard section titles mapped to this block.</param>
/// <param name="HeightWeight">Viewport height weight: "xl", "large", "medium", or "short".</param>
/// <param name="LayoutVariant">Layout variant from the allowed set for the block type.</param>
/// <param name="Panels">Panel definitions within this block.</param>
/// <param name="AssetSlots">Photo-only asset slot prompts for image generation.</param>
/// <param name="DesignOverrides">Optional block-level overrides when this block genuinely departs from the page design system.</param>
public record VisualBlock(
    string BlockId,
    string BlockType,
    string[] SectionRefs,
    string HeightWeight,
    string LayoutVariant,
    LayoutPanel[] Panels,
    AssetSlot[] AssetSlots,
    DesignOverrides? DesignOverrides);

/// <summary>
/// A panel within a visual block defining content area and proportions.
/// </summary>
/// <param name="Role">Semantic role of this panel (e.g., "hero-product", "scene-grab").</param>
/// <param name="HeightRatio">Proportion of the block's height occupied by this panel (0.0–1.0).</param>
/// <param name="ContentType">Content strategy: "copy-with-visual", "visual-only", or "copy-only".</param>
public record LayoutPanel(
    string Role,
    double HeightRatio,
    string ContentType);

/// <summary>
/// A photo-only asset slot prompt for the image generation pipeline.
/// Prompts must describe photographic or illustrative content only — no text, UI elements, or layout.
/// </summary>
/// <param name="SlotId">Unique identifier for this slot within the blueprint.</param>
/// <param name="Prompt">Image generation prompt (min 80 chars, English only, SUBJECT→ENVIRONMENT→LIGHTING→MOOD→COMPOSITION→PALETTE→NEGATIVE SPACE).</param>
/// <param name="AspectRatio">Target aspect ratio (e.g., "4:5", "16:9", "1:1").</param>
/// <param name="PanelRef">Reference to the panel this slot belongs to.</param>
/// <param name="Priority">Generation priority: "high" or "medium".</param>
/// <param name="NegativeConstraints">Constraints for image generation (must include: "no text", "no UI elements", "no buttons", "no captions").</param>
/// <param name="ImageUrl">Generated image URL, populated after asset generation.</param>
public record AssetSlot(
    string SlotId,
    string Prompt,
    string AspectRatio,
    string PanelRef,
    string Priority,
    string[] NegativeConstraints,
    string? ImageUrl);

/// <summary>
/// Block-level design overrides — only fields that genuinely differ from the page design system.
/// Do NOT override brand colors at the block level.
/// </summary>
/// <param name="Mood">Override mood when this block shifts emotional register.</param>
/// <param name="BackgroundApproach">Override background when this block uses a different surface treatment.</param>
/// <param name="TypographyScale">Override typography when this block needs a different text hierarchy.</param>
public record DesignOverrides(
    string? Mood,
    string? BackgroundApproach,
    string? TypographyScale);
