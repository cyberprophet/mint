using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.ValidateBlueprint"/>.
/// Exercises each validation gate individually with targeted invalid inputs.
/// </summary>
public class BlueprintValidationTests
{
    // GptService with a dummy key — ValidateBlueprint never touches the network.
    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    // ─── Helpers ─────────────────────────────────────────────────────────────

    static PageDesignSystem ValidPds() => new(
        Mood: "fresh and modern",
        BrandColors: ["#FFFFFF", "#000000"],
        BackgroundApproach: "dark studio cutout",
        TypographyScale: "display-xl, body-md");

    static AssetSlot ValidSlot(string slotId = "slot-1", string panelRef = "panel-1") => new(
        SlotId: slotId,
        Prompt: "High-key studio photo of a sleek white sneaker on a marble surface, natural side lighting, minimal shadows, clean composition, neutral white palette, generous negative space on right",
        AspectRatio: "4:5",
        PanelRef: panelRef,
        Priority: "high",
        NegativeConstraints: ["no text", "no ui elements", "no buttons", "no captions"],
        ImageUrl: null);

    static LayoutPanel ValidPanel(string role = "main") => new(role, 1.0, "copy-with-visual");

    static VisualBlock ValidHeroBlock(string blockId = "b1") => new(
        BlockId: blockId,
        BlockType: "hero",
        SectionRefs: ["Hero"],
        HeightWeight: "xl",
        LayoutVariant: "full-bleed-center",
        Panels: [ValidPanel()],
        AssetSlots: [ValidSlot()],
        DesignOverrides: null);

    static VisualBlock ValidBlock(string blockId, string blockType = "value-benefit",
        string layoutVariant = "split-feature", string heightWeight = "medium") => new(
        BlockId: blockId,
        BlockType: blockType,
        SectionRefs: [blockId],
        HeightWeight: heightWeight,
        LayoutVariant: layoutVariant,
        Panels: [ValidPanel()],
        AssetSlots: [ValidSlot()],
        DesignOverrides: null);

    // ─── Gate: PageDesignSystem completeness ─────────────────────────────────

    [Fact]
    public void ValidateBlueprint_MissingMood_ReturnsError()
    {
        var pds = ValidPds() with { Mood = "" };
        var blueprint = new BlueprintResult(pds, [ValidHeroBlock()], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("pageDesignSystem.mood is required", error);
    }

    [Fact]
    public void ValidateBlueprint_EmptyBrandColors_ReturnsError()
    {
        var pds = ValidPds() with { BrandColors = [] };
        var blueprint = new BlueprintResult(pds, [ValidHeroBlock()], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("brandColors must have at least 1 color", error);
    }

    [Fact]
    public void ValidateBlueprint_MissingBackgroundApproach_ReturnsError()
    {
        var pds = ValidPds() with { BackgroundApproach = "   " };
        var blueprint = new BlueprintResult(pds, [ValidHeroBlock()], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("pageDesignSystem.backgroundApproach is required", error);
    }

    [Fact]
    public void ValidateBlueprint_MissingTypographyScale_ReturnsError()
    {
        var pds = ValidPds() with { TypographyScale = "" };
        var blueprint = new BlueprintResult(pds, [ValidHeroBlock()], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("pageDesignSystem.typographyScale is required", error);
    }

    // ─── Gate 4: Invalid blockType ────────────────────────────────────────────

    [Fact]
    public void ValidateBlueprint_InvalidBlockType_ReturnsError()
    {
        var block = ValidHeroBlock() with { BlockType = "unknown-block-type" };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("invalid blockType \"unknown-block-type\"", error);
    }

    [Fact]
    public void ValidateBlueprint_InvalidBlockType_SkipsFurtherBlockChecks()
    {
        // When blockType is invalid, further checks for that block are skipped.
        // The error message should reference blockType, not asset slots.
        var block = ValidHeroBlock() with
        {
            BlockType = "bogus",
            AssetSlots = [] // would normally also trigger "must have at least 1 assetSlot"
        };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("invalid blockType", error);
        // Asset slot error should NOT appear — skipped by the continue after Gate 4
        Assert.DoesNotContain("must have at least 1 assetSlot", error);
    }

    // ─── Gate 5: layoutVariant vocabulary ────────────────────────────────────

    [Fact]
    public void ValidateBlueprint_InvalidLayoutVariant_ForHero_ReturnsError()
    {
        var block = ValidHeroBlock() with { LayoutVariant = "nonexistent-variant" };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("layoutVariant \"nonexistent-variant\" is not valid for blockType \"hero\"", error);
        Assert.Contains("full-bleed-center", error); // Allowed variants listed
    }

    // ─── Gate 6: Minimum panel count ─────────────────────────────────────────

    [Fact]
    public void ValidateBlueprint_TriptychWithOnlyOnePanel_ReturnsError()
    {
        var block = new VisualBlock(
            BlockId: "b1",
            BlockType: "vertical-triptych",
            SectionRefs: ["A"],
            HeightWeight: "large",
            LayoutVariant: "three-row-equal",
            Panels: [ValidPanel()], // only 1; min is 2
            AssetSlots: [ValidSlot("s1"), ValidSlot("s2")],
            DesignOverrides: null);

        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("requires at least 2 panel(s), got 1", error);
    }

    // ─── Gate 7: Multi-scene slot-per-panel ──────────────────────────────────

    [Fact]
    public void ValidateBlueprint_TriptychWithFewerSlotsThanPanels_ReturnsError()
    {
        var block = new VisualBlock(
            BlockId: "triptych-1",
            BlockType: "vertical-triptych",
            SectionRefs: ["A"],
            HeightWeight: "large",
            LayoutVariant: "three-row-equal",
            Panels: [ValidPanel("p1"), ValidPanel("p2"), ValidPanel("p3")],
            AssetSlots: [ValidSlot("s1")], // 1 slot for 3 panels — not enough
            DesignOverrides: null);

        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("at least one assetSlot per panel", error);
    }

    // ─── Gate 8: Hero heightWeight must be "xl" ───────────────────────────────

    [Fact]
    public void ValidateBlueprint_HeroWithNonXlHeightWeight_ReturnsError()
    {
        var block = ValidHeroBlock() with { HeightWeight = "medium" };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("hero blocks must use heightWeight \"xl\"", error);
    }

    [Fact]
    public void ValidateBlueprint_HeroWithXlHeightWeight_NoHeightError()
    {
        var block = ValidHeroBlock(); // heightWeight = "xl" by default
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        // May be null (fully valid) or have other errors, but must NOT contain the hero height error
        if (error is not null)
            Assert.DoesNotContain("hero blocks must use heightWeight", error);
    }

    // ─── Gate 9: CTA heightWeight must NOT be xl or large ────────────────────

    [Fact]
    public void ValidateBlueprint_CtaWithXlHeightWeight_ReturnsError()
    {
        var block = new VisualBlock(
            BlockId: "cta-1",
            BlockType: "offer-reassurance-sticky",
            SectionRefs: ["CTA"],
            HeightWeight: "xl",
            LayoutVariant: "product-anchor-cta",
            Panels: [ValidPanel()],
            AssetSlots: [ValidSlot()],
            DesignOverrides: null);

        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("CTA blocks should use \"short\" or \"medium\"", error);
    }

    // ─── Gate 1 (asset slot): Forbidden prompt patterns ──────────────────────

    [Fact]
    public void ValidateBlueprint_PromptWithKoreanText_ReturnsError()
    {
        var slot = ValidSlot() with { Prompt = "한글 텍스트가 포함된 프롬프트입니다, a beautiful photo of a sneaker, natural lighting, white background, minimal, clean palette, negative space on right side, generous margins all around" };
        var block = ValidHeroBlock() with { AssetSlots = [slot] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("prompt matches forbidden pattern", error);
    }

    [Fact]
    public void ValidateBlueprint_PromptWithCollage_ReturnsError()
    {
        var slot = ValidSlot() with { Prompt = "A collage of product shots and lifestyle images, studio lighting, white marble surface, minimal shadows, clean composition, neutral palette, generous negative space on the right hand side of the image" };
        var block = ValidHeroBlock() with { AssetSlots = [slot] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("prompt matches forbidden pattern", error);
    }

    // ─── Gate 2 (asset slot): Prompt minimum length ──────────────────────────

    [Fact]
    public void ValidateBlueprint_PromptTooShort_ReturnsError()
    {
        var slot = ValidSlot() with { Prompt = "Short prompt" }; // < 80 chars
        var block = ValidHeroBlock() with { AssetSlots = [slot] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("prompt too short", error);
        Assert.Contains("min 80", error);
    }

    // ─── Gate 3 (asset slot): Required negativeConstraints ───────────────────

    [Fact]
    public void ValidateBlueprint_MissingNegativeConstraints_ReturnsError()
    {
        var slot = ValidSlot() with { NegativeConstraints = null };
        var block = ValidHeroBlock() with { AssetSlots = [slot] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("negativeConstraints must be specified", error);
    }

    [Fact]
    public void ValidateBlueprint_NegativeConstraintsMissingRequiredTerm_ReturnsError()
    {
        // Missing "no buttons" from required set
        var slot = ValidSlot() with
        {
            NegativeConstraints = ["no text", "no ui elements", "no captions"]
            // "no buttons" is absent
        };
        var block = ValidHeroBlock() with { AssetSlots = [slot] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("no buttons", error);
    }

    // ─── Gate 10: No 3+ consecutive same blockType ───────────────────────────

    [Fact]
    public void ValidateBlueprint_ThreeConsecutiveSameBlockType_ReturnsRhythmError()
    {
        var blocks = new[]
        {
            ValidBlock("b1", "value-benefit", "split-feature"),
            ValidBlock("b2", "value-benefit", "dominant-visual-with-context"),
            ValidBlock("b3", "value-benefit", "single-showcase"),
        };
        var blueprint = new BlueprintResult(ValidPds(), blocks, null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("appears 3 times consecutively", error);
    }

    // ─── Gate 11: No 3+ consecutive same heightWeight ────────────────────────

    [Fact]
    public void ValidateBlueprint_ThreeConsecutiveSameHeightWeight_ReturnsRhythmError()
    {
        var blocks = new[]
        {
            ValidBlock("b1", "value-benefit", "split-feature", heightWeight: "medium"),
            ValidBlock("b2", "proof-trust", "evidence-showcase", heightWeight: "medium"),
            ValidBlock("b3", "benefit-grid", "equal-grid", heightWeight: "medium"),
        };
        var blueprint = new BlueprintResult(ValidPds(), blocks, null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("heightWeight \"medium\" appears 3 times consecutively", error);
    }

    // ─── Gate 14: Block type diversity ───────────────────────────────────────

    [Fact]
    public void ValidateBlueprint_EightBlocksWithOnly3UniqueTypes_ReturnsError()
    {
        // 8 blocks but only 3 unique types — needs ≥5
        var blocks = new[]
        {
            ValidHeroBlock("b1"),
            ValidBlock("b2", "value-benefit", "split-feature", "large"),
            ValidBlock("b3", "proof-trust", "evidence-showcase", "medium"),
            ValidBlock("b4", "value-benefit", "dominant-visual-with-context", "large"),
            ValidBlock("b5", "proof-trust", "exploded-detail", "medium"),
            ValidBlock("b6", "value-benefit", "single-showcase", "medium"),
            ValidBlock("b7", "proof-trust", "multi-evidence-strip", "medium"),
            ValidBlock("b8", "value-benefit", "split-feature", "short"),
        };
        var blueprint = new BlueprintResult(ValidPds(), blocks, null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("unique blockTypes (need ≥5)", error);
    }

    // ─── Gate 15: No repeated layoutVariant for same blockType ───────────────

    [Fact]
    public void ValidateBlueprint_SameBlockTypeWithDuplicateLayoutVariant_ReturnsError()
    {
        var blocks = new[]
        {
            ValidBlock("b1", "value-benefit", "split-feature"),
            ValidBlock("b2", "value-benefit", "split-feature"), // same variant reused
        };
        var blueprint = new BlueprintResult(ValidPds(), blocks, null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("reuses layoutVariant \"split-feature\"", error);
    }

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void ValidateBlueprint_FullyValidBlueprint_ReturnsNull()
    {
        // Minimal valid blueprint: 1 hero block
        var blueprint = new BlueprintResult(ValidPds(), [ValidHeroBlock()], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateBlueprint_NoAssetSlots_ReturnsError()
    {
        var block = ValidHeroBlock() with { AssetSlots = [] };
        var blueprint = new BlueprintResult(ValidPds(), [block], null);

        var error = _sut.ValidateBlueprint(blueprint);

        Assert.NotNull(error);
        Assert.Contains("must have at least 1 assetSlot", error);
    }
}
