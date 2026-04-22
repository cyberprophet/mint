using Microsoft.Extensions.Logging.Abstractions;

using ShareInvest.Agency.Google;
using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// P7 prompt-ownership guardrail tests (ADR-013). Every public entry point that
/// invokes a prompt-driven model call MUST reject null/empty/whitespace prompts with
/// <see cref="ArgumentException"/> (including the <see cref="ArgumentNullException"/>
/// subclass thrown by <c>ArgumentException.ThrowIfNullOrWhiteSpace</c> for null input).
///
/// These tests pass dummy prompt strings only — they never embed real prompt content,
/// per the AGENTS.md policy: "prompts live in P5; P7 tests must use placeholder strings".
/// </summary>
public class PromptValidationTests
{
    const string TestPrompt = "test-prompt";

    // ─── GptService — GenerateTitleAsync ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_GenerateTitleAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.GenerateTitleAsync(badPrompt!, "conversation text"));
    }

    // ─── GptService — AnalyzeImageAsync (Vision) ─────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_AnalyzeImageAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        var bytes = BinaryData.FromBytes([0x89, 0x50, 0x4E, 0x47]);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.AnalyzeImageAsync(badPrompt!, bytes, "image/png"));
    }

    [Fact]
    public async Task GptService_AnalyzeImageAsync_NullImageBytes_Throws()
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.AnalyzeImageAsync(TestPrompt, null!, "image/png"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_AnalyzeImageAsync_BlankMimeType_Throws(string? mime)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        var bytes = BinaryData.FromBytes([0x89, 0x50, 0x4E, 0x47]);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.AnalyzeImageAsync(TestPrompt, bytes, mime!));
    }

    // ─── GptService — ResearchProductAsync ───────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_ResearchProductAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.ResearchProductAsync(badPrompt!, "product info", [], category: null));
    }

    // ─── GptService — GenerateBlueprintAsync ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_BlueprintAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        var context = new BlueprintContext(
            StoryboardJson: "{}",
            VisualDna: null,
            BriefJson: null,
            Feedback: null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.GenerateBlueprintAsync(badPrompt!, context));
    }

    // ─── GptService — GenerateDesignHtmlAsync ────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_DesignHtmlAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        var blueprint = new BlueprintResult(
            PageDesignSystem: new PageDesignSystem("mood", ["#000"], "background", "scale"),
            VisualBlocks: [],
            Assumptions: null);
        var storyboard = new StoryboardResult(Sections: [], CtaText: "cta");
        var context = new DesignHtmlContext(
            Blueprint: blueprint,
            Storyboard: storyboard,
            Brief: null,
            Feedback: null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.GenerateDesignHtmlAsync(badPrompt!, context));
    }

    // ─── GptService — GenerateStoryboardAsync ────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GptService_StoryboardAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        var context = new StoryboardContext(
            Brief: "{}",
            MarketContext: "{}",
            VisualDna: null,
            TargetLanguage: "en",
            ForbiddenCliches: null,
            ProductType: null,
            Feedback: null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.GenerateStoryboardAsync(badPrompt!, context));
    }

    // ─── GeminiProvider — prompt validation ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_GenerateTitleAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.GenerateTitleAsync(badPrompt!, "conversation text"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_AnalyzeImageAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");
        var bytes = BinaryData.FromBytes([0x89, 0x50, 0x4E, 0x47]);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.AnalyzeImageAsync(badPrompt!, bytes, "image/png"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_ExtractProductInfoAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.ExtractProductInfoAsync(
                badPrompt!,
                [new ProductInfoDocument("doc.pdf", "body")]));
    }

    [Fact]
    public async Task GeminiProvider_ExtractProductInfoAsync_NullDocuments_Throws()
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.ExtractProductInfoAsync(TestPrompt, null!));
    }

    [Fact]
    public async Task GeminiProvider_ExtractProductInfoAsync_EmptyDocuments_ReturnsNull()
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");

        var result = await provider.ExtractProductInfoAsync(TestPrompt, []);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_AnalyzeReferenceLinkAsync_BlankSystemPrompt_Throws(string? badPrompt)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");
        var context = new ReferenceLinkContext("ko", null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.AnalyzeReferenceLinkAsync(
                badPrompt!, "https://example.com", "<html/>", context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_AnalyzeReferenceLinkAsync_BlankUrl_Throws(string? url)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");
        var context = new ReferenceLinkContext("ko", null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.AnalyzeReferenceLinkAsync(
                TestPrompt, url!, "<html/>", context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GeminiProvider_AnalyzeReferenceLinkAsync_BlankHtml_Throws(string? html)
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");
        var context = new ReferenceLinkContext("ko", null);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => provider.AnalyzeReferenceLinkAsync(
                TestPrompt, "https://example.com", html!, context));
    }

    [Fact]
    public async Task GeminiProvider_AnalyzeReferenceLinkAsync_NullContext_Throws()
    {
        using var provider = new GeminiProvider(
            NullLogger<GeminiProvider>.Instance, "test-key");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AnalyzeReferenceLinkAsync(
                TestPrompt, "https://example.com", "<html/>", null!));
    }
}
