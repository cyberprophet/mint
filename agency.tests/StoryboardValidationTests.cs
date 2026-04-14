using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.ValidateStoryboard"/> and the
/// related helpers <see cref="GptService.AutoCorrectStoryboard"/> and
/// <see cref="GptService.BuildUserMessage"/> (via reflection).
///
/// ValidateStoryboard is accessed via the internal test hook in GptService by
/// subclassing so we can call the non-public virtual helper through the real method
/// signature. Since the method is declared with a non-public accessor we use
/// reflection to reach it directly — consistent with the existing
/// BlueprintValidationTests pattern.
/// </summary>
public class StoryboardValidationTests
{
    // GptService with a dummy key — ValidateStoryboard / AutoCorrectStoryboard never
    // touch the network.
    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    // ─── Reflection handles ───────────────────────────────────────────────────

    static readonly System.Reflection.MethodInfo ValidateStoryboardMethod =
        typeof(GptService).GetMethod("ValidateStoryboard",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? throw new MissingMethodException(nameof(GptService), "ValidateStoryboard");

    static readonly System.Reflection.MethodInfo AutoCorrectStoryboardMethod =
        typeof(GptService).GetMethod("AutoCorrectStoryboard",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(GptService), "AutoCorrectStoryboard");

    static readonly System.Reflection.MethodInfo BuildUserMessageMethod =
        typeof(GptService).GetMethod("BuildUserMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(GptService), "BuildUserMessage");

    /// <summary>
    /// Invokes ValidateStoryboard(storyboard, targetLanguage, forbiddenCliches, productType, autoCorrect).
    /// </summary>
    string? Validate(StoryboardResult storyboard,
        string targetLanguage = "en",
        string[]? forbiddenCliches = null,
        string? productType = null,
        bool autoCorrect = false)
    {
        return (string?)ValidateStoryboardMethod.Invoke(_sut,
            [storyboard, targetLanguage, forbiddenCliches, productType, autoCorrect]);
    }

    static StoryboardResult AutoCorrect(StoryboardResult storyboard)
    {
        return (StoryboardResult)AutoCorrectStoryboardMethod.Invoke(null, [storyboard])!;
    }

    static string BuildUserMessage(StoryboardContext context)
    {
        return (string)BuildUserMessageMethod.Invoke(null, [context])!;
    }

    // ─── Shared builders ──────────────────────────────────────────────────────

    /// <summary>Returns an image block with a 100+ char English prompt.</summary>
    static StoryboardBlock ImageBlock(string? content = null) => new(
        Type: "image",
        Content: content ??
            "Elegant product studio shot of a glass perfume bottle on polished white marble, " +
            "soft side-lighting from left, shallow depth of field, warm neutral tones, centered composition, negative space on right");

    static StoryboardSection SectionWithImage(
        string title = "Hero",
        string strategicIntent = "Capture attention",
        string? sectionType = "hero") =>
        new(title, strategicIntent, sectionType,
            Blocks: [new("heading", "Title here"), ImageBlock()]);

    static StoryboardSection SectionWithoutImage(
        string title = "Benefits",
        string sectionType = "value") =>
        new(title, "Highlight benefits", sectionType,
            Blocks: [new("heading", "Benefit A"), new("text", "Detail about benefit A.")]);

    /// <summary>Minimal valid storyboard with faq and spec-table for a physical product.</summary>
    static StoryboardResult MinimalValidStoryboard() => new(
        Sections:
        [
            SectionWithImage("Hero", "Capture attention", "hero"),
            SectionWithImage("FAQ", "Answer questions", "faq"),
            SectionWithImage("Specs", "Show specs", "spec-table"),
        ],
        CtaText: "구매하기");

    // ─── Gate 1: Every section must have at least one image block ─────────────

    [Fact]
    public void Validate_SectionWithoutImageBlock_ReturnsError()
    {
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithoutImage("Hero", "hero"),
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Buy Now");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("missing type: \"image\" blocks", error);
        Assert.Contains("Hero", error);
    }

    [Fact]
    public void Validate_SectionWithoutImageBlock_AutoCorrect_DoesNotReturnImageError()
    {
        // When autoCorrect=true, missing image blocks are already inserted by AutoCorrectStoryboard
        // before Validate is called. If caller passes autoCorrect=true the error is suppressed.
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithoutImage("Benefits", "value"),
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Order Now");

        var error = Validate(storyboard, autoCorrect: true);

        // autoCorrect=true suppresses gate-1 image-block error
        if (error is not null)
            Assert.DoesNotContain("missing type: \"image\" blocks", error);
    }

    // ─── Gate 2: Image prompt must be ≥70% Latin ─────────────────────────────

    [Fact]
    public void Validate_ImagePromptPrimarilyKorean_ReturnsError()
    {
        var koreanPrompt =
            "아름다운 제품 사진으로 흰색 배경에 화장품 병을 촬영한 것입니다. " +
            "조명은 자연스럽고 구성은 최소화되어 있습니다. 네거티브 스페이스가 충분합니다.";

        var section = new StoryboardSection(
            "Hero", "Test", "hero",
            Blocks: [new("image", koreanPrompt)]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "구매하기");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("not primarily English", error);
        Assert.Contains("Hero", error);
    }

    [Fact]
    public void Validate_ImagePromptPrimarilyEnglish_NoLanguageError()
    {
        var storyboard = MinimalValidStoryboard();

        var error = Validate(storyboard);

        // No image language error expected
        if (error is not null)
            Assert.DoesNotContain("not primarily English", error);
    }

    // ─── Gate 3: Image prompt minimum length (100 chars) ─────────────────────

    [Fact]
    public void Validate_ShortImagePrompt_ReturnsError()
    {
        var shortPrompt = "A product on a white background."; // < 100 chars

        var section = new StoryboardSection(
            "Hero", "Test", "hero",
            Blocks: [new("image", shortPrompt)]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Buy");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("too short", error);
        Assert.Contains("min 100", error);
    }

    [Fact]
    public void Validate_ImagePromptAtMinimumLength_NoLengthError()
    {
        // Exactly 100 chars
        var prompt = new string('A', 90) + " on white background setup.";
        Assert.True(prompt.Trim().Length >= 100);

        var section = new StoryboardSection(
            "Hero", "Test", "hero",
            Blocks: [new("image", prompt)]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Buy");

        var error = Validate(storyboard);

        if (error is not null)
            Assert.DoesNotContain("too short", error);
    }

    // ─── Gate 4: Copy cliché detection ───────────────────────────────────────

    [Fact]
    public void Validate_KoreanClicheInCopyBlock_ReturnsError()
    {
        var section = new StoryboardSection(
            "Hero", "Capture attention", "hero",
            Blocks:
            [
                new("heading", "차이를 경험하세요"),
                ImageBlock()
            ]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "구매");

        var error = Validate(storyboard, targetLanguage: "ko");

        Assert.NotNull(error);
        Assert.Contains("Generic copy", error);
        Assert.Contains("차이를 경험하세요", error);
    }

    [Fact]
    public void Validate_EnglishClicheInCopyBlock_ReturnsError()
    {
        var section = new StoryboardSection(
            "Hero", "Capture attention", "hero",
            Blocks:
            [
                new("heading", "Discover the difference today and forever."),
                ImageBlock()
            ]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Buy Now");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("Generic copy", error);
    }

    [Fact]
    public void Validate_ForbiddenCliches_DetectedInCopyBlock()
    {
        var section = new StoryboardSection(
            "Hero", "Capture attention", "hero",
            Blocks:
            [
                new("heading", "Our product is a real game-changer for athletes."),
                ImageBlock()
            ]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Order");

        // "game-changer" is in GenericCopyPatterns
        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("game", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_PerProductForbiddenCliche_DetectedInCopyBlock()
    {
        var section = new StoryboardSection(
            "Benefits", "Highlight features", "value",
            Blocks:
            [
                new("text", "This is truly an amazing skincare revolution."),
                ImageBlock()
            ]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Order");

        var error = Validate(storyboard,
            forbiddenCliches: ["skincare revolution"],
            targetLanguage: "en");

        Assert.NotNull(error);
        Assert.Contains("Forbidden cliché", error);
        Assert.Contains("skincare revolution", error);
    }

    // ─── Gate 5: Image prompt self-containment ────────────────────────────────

    [Fact]
    public void Validate_ImagePromptContainsExternalRef_ReturnsError()
    {
        var prompt =
            "As mentioned above, the product shown earlier in the product section " +
            "with white background and studio lighting and clean minimal composition and generous space.";

        var section = new StoryboardSection(
            "Hero", "Test", "hero",
            Blocks: [new("image", prompt)]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "Buy");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("references external context", error);
    }

    // ─── Gate 8: Required sections (faq, spec-table) ─────────────────────────

    [Fact]
    public void Validate_MissingFaqSection_ReturnsError()
    {
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithImage("Hero", "Test", "hero"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
                // no faq section
            ],
            CtaText: "Buy");

        var error = Validate(storyboard);

        Assert.NotNull(error);
        Assert.Contains("Missing required section: \"faq\"", error);
    }

    [Fact]
    public void Validate_MissingSpecTableForPhysicalProduct_ReturnsError()
    {
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithImage("Hero", "Test", "hero"),
                SectionWithImage("FAQ", "Answer questions", "faq"),
                // no spec-table section
            ],
            CtaText: "Buy");

        var error = Validate(storyboard, productType: "skincare");

        Assert.NotNull(error);
        Assert.Contains("Missing required section: \"spec-table\"", error);
    }

    [Fact]
    public void Validate_MissingSpecTable_DigitalProduct_NoError()
    {
        // Digital products (subscription, saas, etc.) are exempt from spec-table requirement
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithImage("Hero", "Test", "hero"),
                SectionWithImage("FAQ", "Answer questions", "faq"),
                // no spec-table — digital product is exempt
            ],
            CtaText: "Subscribe");

        var error = Validate(storyboard, productType: "subscription");

        if (error is not null)
            Assert.DoesNotContain("spec-table", error);
    }

    [Theory]
    [InlineData("구독")]
    [InlineData("subscription")]
    [InlineData("saas")]
    [InlineData("software")]
    [InlineData("digital")]
    [InlineData("ebook")]
    [InlineData("membership")]
    public void Validate_VariousDigitalProductTypes_ExemptFromSpecTable(string productType)
    {
        var storyboard = new StoryboardResult(
            Sections:
            [
                SectionWithImage("Hero", "Test", "hero"),
                SectionWithImage("FAQ", "Answer questions", "faq"),
            ],
            CtaText: "Start");

        var error = Validate(storyboard, productType: productType);

        if (error is not null)
            Assert.DoesNotContain("spec-table", error);
    }

    // ─── Gate 7: Copy block language validation ───────────────────────────────

    [Fact]
    public void Validate_EnglishCopyBlockWhenTargetIsKorean_ReturnsError()
    {
        // When targetLanguage is "ko", heading/text blocks that are primarily Latin are rejected.
        var section = new StoryboardSection(
            "Hero", "Capture attention", "hero",
            Blocks:
            [
                new("heading", "This heading is entirely written in English language."),
                ImageBlock()
            ]);

        var storyboard = new StoryboardResult(
            Sections:
            [
                section,
                SectionWithImage("FAQ", "Answer questions", "faq"),
                SectionWithImage("Specs", "Show specs", "spec-table"),
            ],
            CtaText: "구매");

        var error = Validate(storyboard, targetLanguage: "ko");

        Assert.NotNull(error);
        Assert.Contains("appears to be in English", error);
        Assert.Contains("Korean", error);
    }

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidStoryboard_ReturnsNull()
    {
        var storyboard = MinimalValidStoryboard();

        var error = Validate(storyboard);

        Assert.Null(error);
    }

    // ─── AutoCorrectStoryboard ────────────────────────────────────────────────

    [Fact]
    public void AutoCorrect_SectionWithoutImageBlock_InsertsPlaceholder()
    {
        var sectionWithout = SectionWithoutImage("Benefits", "value");
        var storyboard = new StoryboardResult(
            Sections: [sectionWithout],
            CtaText: "Buy");

        var corrected = AutoCorrect(storyboard);

        // The corrected section must now contain an image block
        var correctedSection = corrected.Sections[0];
        var hasImage = correctedSection.Blocks.Any(b =>
            string.Equals(b.Type, "image", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasImage, "AutoCorrect should insert a placeholder image block");
    }

    [Fact]
    public void AutoCorrect_SectionWithImageBlock_IsNotModified()
    {
        var original = SectionWithImage("Hero", "Capture", "hero");
        var originalBlockCount = original.Blocks.Length;

        var storyboard = new StoryboardResult(
            Sections: [original],
            CtaText: "Buy");

        var corrected = AutoCorrect(storyboard);

        // Section already has an image block — block count must not change
        Assert.Equal(originalBlockCount, corrected.Sections[0].Blocks.Length);
    }

    [Fact]
    public void AutoCorrect_PlaceholderPrompt_ContainsStrategicIntent()
    {
        var section = new StoryboardSection(
            "Benefits", "highlight the key ingredient story", "value",
            Blocks: [new("heading", "Clean beauty.")]);

        var storyboard = new StoryboardResult(
            Sections: [section],
            CtaText: "Buy");

        var corrected = AutoCorrect(storyboard);

        var imageBlock = corrected.Sections[0].Blocks
            .First(b => string.Equals(b.Type, "image", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("highlight the key ingredient story", imageBlock.Content);
    }

    [Fact]
    public void AutoCorrect_PreservesCtaText()
    {
        var storyboard = new StoryboardResult(
            Sections: [SectionWithoutImage()],
            CtaText: "지금 주문하기");

        var corrected = AutoCorrect(storyboard);

        Assert.Equal("지금 주문하기", corrected.CtaText);
    }

    [Fact]
    public void AutoCorrect_MixedSections_OnlyCorrectsThoseWithoutImage()
    {
        var withImage = SectionWithImage("Hero", "Capture", "hero");
        var withoutImage = SectionWithoutImage("Details", "value");

        var storyboard = new StoryboardResult(
            Sections: [withImage, withoutImage],
            CtaText: "Buy");

        var corrected = AutoCorrect(storyboard);

        // First section already had an image — block count unchanged
        Assert.Equal(withImage.Blocks.Length, corrected.Sections[0].Blocks.Length);

        // Second section was missing image — one block added
        Assert.Equal(withoutImage.Blocks.Length + 1, corrected.Sections[1].Blocks.Length);
    }

    // ─── BuildUserMessage (storyboard prompt assembly) ────────────────────────

    [Fact]
    public void BuildUserMessage_ContainsBriefSection()
    {
        var context = new StoryboardContext(
            Brief: "A high-performance running shoe for elite athletes.",
            MarketContext: "Premium athletic market, 25-40 year-olds.",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        Assert.Contains("## Brief", message);
        Assert.Contains("high-performance running shoe", message);
    }

    [Fact]
    public void BuildUserMessage_ContainsMarketContext()
    {
        var context = new StoryboardContext(
            Brief: "Product brief.",
            MarketContext: "Premium market segment focus.",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        Assert.Contains("## Market Context", message);
        Assert.Contains("Premium market segment focus", message);
    }

    [Fact]
    public void BuildUserMessage_WithVisualDna_IncludesSection()
    {
        var context = new StoryboardContext(
            Brief: "Brief.",
            MarketContext: "Context.",
            VisualDna: "Premium minimal aesthetic, white studio.",
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        Assert.Contains("## Visual DNA", message);
        Assert.Contains("Premium minimal aesthetic", message);
    }

    [Fact]
    public void BuildUserMessage_WithoutVisualDna_NoVisualDnaSection()
    {
        var context = new StoryboardContext(
            Brief: "Brief.",
            MarketContext: "Context.",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        // Visual DNA section is only included when the value is not empty
        Assert.DoesNotContain("## Visual DNA", message);
    }

    [Fact]
    public void BuildUserMessage_ContainsTargetLanguageInstruction()
    {
        var context = new StoryboardContext(
            Brief: "Brief.",
            MarketContext: "Context.",
            VisualDna: null,
            TargetLanguage: "ko",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        Assert.Contains("Target Language: ko", message);
        Assert.Contains("ko", message);
    }

    [Fact]
    public void BuildUserMessage_WithFeedback_IncludesFeedbackSection()
    {
        var context = new StoryboardContext(
            Brief: "Brief.",
            MarketContext: "Context.",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: "Fix: FAQ section was missing.");

        var message = BuildUserMessage(context);

        Assert.Contains("## Previous Validation Error", message);
        Assert.Contains("FAQ section was missing", message);
    }

    [Fact]
    public void BuildUserMessage_WithoutFeedback_NoPreviousValidationErrorSection()
    {
        var context = new StoryboardContext(
            Brief: "Brief.",
            MarketContext: "Context.",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        Assert.DoesNotContain("Previous Validation Error", message);
    }

    [Fact]
    public void BuildUserMessage_UserInputIsWrappedInDelimiters()
    {
        // All user-supplied fields are passed through PromptSanitizer.EscapeForPrompt
        var context = new StoryboardContext(
            Brief: "Test brief content",
            MarketContext: "Test market",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        var message = BuildUserMessage(context);

        // PromptSanitizer wraps text in <user_input> delimiters
        Assert.Contains("<user_input>", message);
        Assert.Contains("</user_input>", message);
    }
}
