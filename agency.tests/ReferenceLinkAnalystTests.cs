using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using OpenAI.Chat;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="GptService.AnalyzeReferenceLinkAsync"/> (Intent 041 Phase A).
/// The OpenAI network call is intercepted via a subclass override so tests run offline.
/// </summary>
public class ReferenceLinkAnalystTests
{
    // ─── Helpers / fixtures ───────────────────────────────────────────────────

    static readonly ReferenceLinkContext DefaultContext = new("ko", "Premium Moisturiser");

    const string SampleUrl = "https://example.com/landing";

    const string SampleHtml = """
        <html>
        <head><title>Best Skincare</title></head>
        <body>
        <h1>The finest formula</h1>
        <p>Radiant skin in 14 days. Science-backed, dermatologist tested.</p>
        <p>Free shipping. 30-day return. Over 10,000 happy customers.</p>
        <a href="/buy">Buy Now</a>
        </body>
        </html>
        """;

    static ReferenceLinkAnalysis MakeValidAnalysis() => new(
        LayoutPattern: "hero-benefit-cta",
        CopyTone: "warm-editorial",
        ColorPalette: ["#FFFFFF", "#F0E6D3", "#3A3A3A"],
        TypographyStyle: "sans-minimal",
        MessagingAngles: ["Radiant skin in 14 days", "Dermatologist tested", "30-day return policy"],
        RawSummary: "A clean, editorial skincare landing page. Strong trust signals and clear CTA make it highly effective as a reference.");

    // ─── Input validation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeReferenceLinkAsync_BlankUrl_Throws(string? url)
    {
        var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.AnalyzeReferenceLinkAsync(url!, SampleHtml, DefaultContext));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeReferenceLinkAsync_BlankHtml_Throws(string? html)
    {
        var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.AnalyzeReferenceLinkAsync(SampleUrl, html!, DefaultContext));
    }

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_NullContext_Throws()
    {
        var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.AnalyzeReferenceLinkAsync(SampleUrl, SampleHtml, null!));
    }

    // ─── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_WellFormedResponse_ReturnsParsedDto()
    {
        var expected = MakeValidAnalysis();
        var usageEvents = new List<ApiUsageEvent>();

        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.ToolCalls],
            [expected]);

        var result = await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        Assert.NotNull(result);
        Assert.Equal("hero-benefit-cta", result!.LayoutPattern);
        Assert.Equal("warm-editorial", result.CopyTone);
        Assert.Equal(3, result.ColorPalette.Length);
        Assert.Equal("sans-minimal", result.TypographyStyle);
        Assert.Equal(3, result.MessagingAngles.Length);
        Assert.False(string.IsNullOrWhiteSpace(result.RawSummary));
    }

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_WellFormedResponse_InvokesOnUsageOnce()
    {
        var expected = MakeValidAnalysis();
        var usageEvents = new List<ApiUsageEvent>();

        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.ToolCalls],
            [expected]);

        await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        Assert.Single(usageEvents);
        Assert.Equal("openai", usageEvents[0].Provider);
        Assert.Equal("gpt-5.4", usageEvents[0].Model);
        Assert.Equal("reference_link", usageEvents[0].Purpose);
    }

    // ─── Validation retry ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_FirstAttemptMissingFields_RetrySucceeds()
    {
        // First response: analysis with empty LayoutPattern (fails validation)
        var invalid = MakeValidAnalysis() with { LayoutPattern = "" };
        var valid = MakeValidAnalysis();
        var usageEvents = new List<ApiUsageEvent>();

        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.ToolCalls, ChatFinishReason.ToolCalls],
            [invalid, valid]);

        var result = await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        Assert.NotNull(result);
        Assert.Equal("hero-benefit-cta", result!.LayoutPattern);

        // onUsage must have been called once per OpenAI round-trip
        Assert.Equal(2, usageEvents.Count);
    }

    // ─── Exhausted retries ────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_AllAttemptsInvalid_ReturnsNull()
    {
        var invalid = MakeValidAnalysis() with { LayoutPattern = "" };
        var usageEvents = new List<ApiUsageEvent>();

        // Always return an invalid response (3 retries max)
        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.ToolCalls, ChatFinishReason.ToolCalls, ChatFinishReason.ToolCalls],
            [invalid, invalid, invalid]);

        var result = await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        Assert.Null(result);
        Assert.Equal(3, usageEvents.Count);
    }

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_ModelReturnsText_ExhaustsRetries_ReturnsNull()
    {
        var usageEvents = new List<ApiUsageEvent>();

        // Model keeps returning Stop (plain text) instead of calling the tool
        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.Stop, ChatFinishReason.Stop, ChatFinishReason.Stop],
            []);

        var result = await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        Assert.Null(result);
        Assert.Equal(3, usageEvents.Count);
    }

    // ─── onUsage per call ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeReferenceLinkAsync_OnUsageCalledOncePerOpenAiRound()
    {
        var expected = MakeValidAnalysis();
        var usageEvents = new List<ApiUsageEvent>();

        var svc = new FakeReferenceLinkGptService(
            NullLogger<GptService>.Instance, "test-key",
            [ChatFinishReason.ToolCalls],
            [expected]);

        await svc.AnalyzeReferenceLinkAsync(
            SampleUrl, SampleHtml, DefaultContext,
            onUsage: usageEvents.Add);

        // Single-round success → exactly 1 usage event
        Assert.Single(usageEvents);
    }

    // ─── Static helper: StripHtmlNoise ────────────────────────────────────────

    [Fact]
    public void StripHtmlNoise_RemovesScriptTags()
    {
        var html = "<div>hello</div><script>alert('x')</script><p>world</p>";
        var result = GptService.StripHtmlNoise(html);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("hello", result);
        Assert.Contains("world", result);
    }

    [Fact]
    public void StripHtmlNoise_RemovesStyleTags()
    {
        var html = "<div>content</div><style>body { color: red; }</style>";
        var result = GptService.StripHtmlNoise(html);
        Assert.DoesNotContain("color: red", result);
        Assert.Contains("content", result);
    }

    [Fact]
    public void StripHtmlNoise_RemovesHtmlComments()
    {
        var html = "<div>visible</div><!-- hidden comment -->";
        var result = GptService.StripHtmlNoise(html);
        Assert.DoesNotContain("hidden comment", result);
        Assert.Contains("visible", result);
    }

    [Fact]
    public void StripHtmlNoise_PreservesBodyText()
    {
        var result = GptService.StripHtmlNoise(SampleHtml);
        Assert.Contains("The finest formula", result);
        Assert.Contains("Buy Now", result);
    }

    // ─── Static helper: ValidateReferenceLinkAnalysis ─────────────────────────

    [Fact]
    public void ValidateReferenceLinkAnalysis_ValidInput_ReturnsNull()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(MakeValidAnalysis());
        Assert.Null(error);
    }

    [Fact]
    public void ValidateReferenceLinkAnalysis_NullAnalysis_ReturnsError()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(null);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateReferenceLinkAnalysis_BlankLayoutPattern_ReturnsError(string value)
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { LayoutPattern = value });
        Assert.NotNull(error);
        Assert.Contains("layoutPattern", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateReferenceLinkAnalysis_BlankCopyTone_ReturnsError(string value)
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { CopyTone = value });
        Assert.NotNull(error);
        Assert.Contains("copyTone", error);
    }

    [Fact]
    public void ValidateReferenceLinkAnalysis_EmptyColorPalette_ReturnsError()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { ColorPalette = [] });
        Assert.NotNull(error);
        Assert.Contains("colorPalette", error);
    }

    [Fact]
    public void ValidateReferenceLinkAnalysis_TooManyColors_ReturnsError()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { ColorPalette = ["#1", "#2", "#3", "#4", "#5", "#6", "#7"] });
        Assert.NotNull(error);
        Assert.Contains("colorPalette", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateReferenceLinkAnalysis_BlankTypographyStyle_ReturnsError(string value)
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { TypographyStyle = value });
        Assert.NotNull(error);
        Assert.Contains("typographyStyle", error);
    }

    [Fact]
    public void ValidateReferenceLinkAnalysis_EmptyMessagingAngles_ReturnsError()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { MessagingAngles = [] });
        Assert.NotNull(error);
        Assert.Contains("messagingAngles", error);
    }

    [Fact]
    public void ValidateReferenceLinkAnalysis_TooManyMessagingAngles_ReturnsError()
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { MessagingAngles = ["a", "b", "c", "d"] });
        Assert.NotNull(error);
        Assert.Contains("messagingAngles", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateReferenceLinkAnalysis_BlankRawSummary_ReturnsError(string value)
    {
        var error = GptService.ValidateReferenceLinkAnalysis(
            MakeValidAnalysis() with { RawSummary = value });
        Assert.NotNull(error);
        Assert.Contains("rawSummary", error);
    }

    // ─── DTO round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void ReferenceLinkAnalysis_JsonRoundTrip_PreservesAllFields()
    {
        var original = MakeValidAnalysis();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<ReferenceLinkAnalysis>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.LayoutPattern, deserialized!.LayoutPattern);
        Assert.Equal(original.CopyTone, deserialized.CopyTone);
        Assert.Equal(original.ColorPalette, deserialized.ColorPalette);
        Assert.Equal(original.TypographyStyle, deserialized.TypographyStyle);
        Assert.Equal(original.MessagingAngles, deserialized.MessagingAngles);
        Assert.Equal(original.RawSummary, deserialized.RawSummary);
    }

    [Fact]
    public void ReferenceLinkContext_WithoutProductName_ProductNameIsNull()
    {
        var ctx = new ReferenceLinkContext("en");
        Assert.Null(ctx.ProductName);
        Assert.Equal("en", ctx.TargetLanguage);
    }

    // ─── Fake service (test double) ───────────────────────────────────────────

    /// <summary>
    /// A test-double subclass of <see cref="GptService"/> that intercepts
    /// <see cref="AnalyzeReferenceLinkAsync"/> and drives a scripted response
    /// sequence without touching the OpenAI API.
    /// </summary>
    sealed class FakeReferenceLinkGptService(
        Microsoft.Extensions.Logging.ILogger<GptService> logger,
        string apiKey,
        IReadOnlyList<ChatFinishReason> finishReasons,
        IReadOnlyList<ReferenceLinkAnalysis?> responses)
        : GptService(logger, apiKey)
    {
        int _callIndex;

        public override async Task<ReferenceLinkAnalysis?> AnalyzeReferenceLinkAsync(
            string url,
            string html,
            ReferenceLinkContext context,
            Action<ApiUsageEvent>? onUsage = null,
            CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            ArgumentException.ThrowIfNullOrWhiteSpace(html);
            ArgumentNullException.ThrowIfNull(context);

            const string model = "gpt-5.4";
            const int maxRetries = 3;

            for (int attempt = 0; attempt < maxRetries && _callIndex < finishReasons.Count; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var reason = finishReasons[_callIndex];

                // Simulate token usage emission
                onUsage?.Invoke(new ApiUsageEvent("openai", model, 500, 200, "reference_link"));

                if (reason == ChatFinishReason.Stop)
                {
                    // Model returned text — simulate the retry
                    _callIndex++;
                    continue;
                }

                if (reason == ChatFinishReason.ToolCalls && _callIndex < responses.Count)
                {
                    var candidate = responses[_callIndex];
                    _callIndex++;

                    var validationError = ValidateReferenceLinkAnalysis(candidate);

                    if (validationError is not null)
                    {
                        // Simulate validation failure path — will retry on next iteration
                        continue;
                    }

                    return candidate;
                }

                _callIndex++;
            }

            return null;
        }
    }
}
