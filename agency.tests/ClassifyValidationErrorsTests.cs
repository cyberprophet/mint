using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.ClassifyValidationErrors"/>.
/// Validates that the diagnostic classifier correctly categorizes validation error strings
/// for Intent 037 Phase A structured logging.
/// </summary>
public class ClassifyValidationErrorsTests
{
    [Fact]
    public void SingleLine_VisualBlocksConstraint_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] visualBlocks must contain at least one block.");

        Assert.Single(result);
        Assert.Equal("visualBlocks_constraint", result[0]);
    }

    [Fact]
    public void SingleLine_PageDesignSystemIncomplete_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] pageDesignSystem.mood is required.");

        Assert.Single(result);
        Assert.Equal("pageDesignSystem_incomplete", result[0]);
    }

    [Fact]
    public void SingleLine_InvalidBlockType_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Block \"b1\": invalid blockType \"carousel\". Must be one of: hero, vertical-triptych");

        Assert.Single(result);
        Assert.Equal("invalid_blockType", result[0]);
    }

    [Fact]
    public void SingleLine_LayoutVariant_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Block \"b1\": layoutVariant \"center\" is not valid for blockType \"hero\". Allowed: full-bleed-center");

        Assert.Single(result);
        Assert.Equal("invalid_layoutVariant", result[0]);
    }

    [Fact]
    public void SingleLine_ForbiddenPromptPattern_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Asset slot \"s1\" in block \"b1\": prompt matches forbidden pattern \"Korean\\s+text\".");

        Assert.Single(result);
        Assert.Equal("forbidden_prompt_pattern", result[0]);
    }

    [Fact]
    public void SingleLine_PromptTooShort_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Asset slot \"s1\" in block \"b1\": prompt too short (40 chars, min 80).");

        Assert.Single(result);
        Assert.Equal("prompt_too_short", result[0]);
    }

    [Fact]
    public void SingleLine_NegativeConstraints_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Asset slot \"s1\" in block \"b1\": negativeConstraints must include \"no text\".");

        Assert.Single(result);
        Assert.Equal("missing_negativeConstraints", result[0]);
    }

    [Fact]
    public void SingleLine_RhythmBlockType_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Rhythm Error] blockType \"value-benefit\" appears 3 times consecutively (blocks 2–4).");

        Assert.Single(result);
        Assert.Equal("rhythm_blockType", result[0]);
    }

    [Fact]
    public void SingleLine_RhythmHeightWeight_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Rhythm Error] heightWeight \"medium\" appears 3 times consecutively (blocks 2–4).");

        Assert.Single(result);
        Assert.Equal("rhythm_heightWeight", result[0]);
    }

    [Fact]
    public void SingleLine_LowDiversity_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "Page has 8 blocks but only 3 unique blockTypes (need ≥5). Vary block types for visual diversity.");

        Assert.Single(result);
        Assert.Equal("low_blockType_diversity", result[0]);
    }

    [Fact]
    public void SingleLine_MissingImageBlock_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] The following sections are missing type: \"image\" blocks: Hero, FAQ.");

        Assert.Single(result);
        Assert.Equal("missing_image_block", result[0]);
    }

    [Fact]
    public void SingleLine_GenericCopy_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Generic copy in \"Hero\" / text: \"차이를 경험하세요\" → Describe the specific difference");

        Assert.Single(result);
        Assert.Equal("generic_copy", result[0]);
    }

    [Fact]
    public void SingleLine_MissingRequiredSection_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Missing required section: \"faq\".");

        Assert.Single(result);
        Assert.Equal("missing_required_section", result[0]);
    }

    [Fact]
    public void MultiLine_DeduplicatesCategories()
    {
        var error = "[Validation Error] pageDesignSystem.mood is required.\n" +
                    "[Validation Error] pageDesignSystem.brandColors must have at least 1 color.";

        var result = GptService.ClassifyValidationErrors(error);

        // Both lines match pageDesignSystem_incomplete — should be deduplicated
        Assert.Single(result);
        Assert.Equal("pageDesignSystem_incomplete", result[0]);
    }

    [Fact]
    public void MultiLine_MultipleDifferentCategories()
    {
        var error = "[Validation Error] pageDesignSystem.mood is required.\n" +
                    "[Validation Error] Asset slot \"s1\" in block \"b1\": prompt too short (40 chars, min 80).\n" +
                    "[Rhythm Error] blockType \"value-benefit\" appears 3 times consecutively (blocks 2–4).";

        var result = GptService.ClassifyValidationErrors(error);

        Assert.Equal(3, result.Length);
        Assert.Contains("pageDesignSystem_incomplete", result);
        Assert.Contains("prompt_too_short", result);
        Assert.Contains("rhythm_blockType", result);
    }

    [Fact]
    public void EmptyString_ReturnsUnknown()
    {
        var result = GptService.ClassifyValidationErrors("");

        Assert.Single(result);
        Assert.Equal("unknown", result[0]);
    }

    [Fact]
    public void UnrecognizedError_ReturnsOther()
    {
        var result = GptService.ClassifyValidationErrors("Something unexpected happened.");

        Assert.Single(result);
        Assert.Equal("other", result[0]);
    }

    [Fact]
    public void SingleLine_CopyWrongLanguage_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Copy block in \"Hero\" appears to be in English but target language is Korean.");

        Assert.Single(result);
        Assert.Equal("copy_wrong_language", result[0]);
    }

    [Fact]
    public void SingleLine_ImagePromptNotEnglish_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Image prompt in \"Hero\" is not primarily English (\"전문적인 제품 사진...\").");

        Assert.Single(result);
        Assert.Equal("image_prompt_not_english", result[0]);
    }

    [Fact]
    public void SingleLine_RepeatedLayoutVariant_Classified()
    {
        var result = GptService.ClassifyValidationErrors(
            "blockType \"value-benefit\" reuses layoutVariant \"split-feature\". Use different variants.");

        Assert.Single(result);
        Assert.Equal("repeated_layoutVariant", result[0]);
    }

    [Fact]
    public void SingleLine_SlotPerPanel_ShouldHave_Classified()
    {
        // Tests the actual validator output from GptService.Blueprint.cs (Gate 7)
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Block \"b3\": blockType \"vertical-triptych\" should have " +
            "at least one assetSlot per panel (3 panels, 1 slots). " +
            "Each scene/step needs its own photo — do not combine multiple scenes into one image.");

        Assert.Single(result);
        Assert.Equal("insufficient_panels", result[0]);
    }

    [Fact]
    public void SingleLine_SlotPerPanel_RequiresAtLeast_Classified()
    {
        // Tests the "requires at least" variant (Gate 6: minimum panel count)
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Block \"b2\": blockType \"vertical-triptych\" requires " +
            "at least 2 panel(s), got 1.");

        Assert.Single(result);
        Assert.Equal("insufficient_panels", result[0]);
    }

    [Fact]
    public void SingleLine_MissingAssetSlot_Classified()
    {
        // Tests the actual validator output: "must have at least 1 assetSlot"
        var result = GptService.ClassifyValidationErrors(
            "[Validation Error] Block \"b1\": must have at least 1 assetSlot.");

        Assert.Single(result);
        Assert.Equal("missing_assetSlot", result[0]);
    }
}
