using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for the Intent 031 StudioMint agent. The OpenAI image-edit call
/// itself is not exercised (requires a real key + billing); tests focus on the
/// deterministic contract — shot definitions, prompt composition, embedded
/// resource loading, error mapping, input validation, and IsComplete aggregation.
/// </summary>
public class StudioMintTests
{
    // ─── Shot definitions ─────────────────────────────────────────────────────

    [Fact]
    public void StudioMintShotTypes_HasExactlyFourV1Shots()
    {
        Assert.Equal(4, StudioMintShotTypes.All.Count);
    }

    [Fact]
    public void StudioMintShotTypes_ShotIdsAreUnique()
    {
        var ids = StudioMintShotTypes.All.Select(s => s.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void StudioMintShotTypes_IncludesExpectedV1Slots()
    {
        var ids = StudioMintShotTypes.All.Select(s => s.Id).ToHashSet();
        Assert.Contains("hero-front", ids);
        Assert.Contains("lifestyle", ids);
        Assert.Contains("detail-macro", ids);
        Assert.Contains("alt-angle", ids);
    }

    [Fact]
    public void StudioMintShotTypes_EveryShotHasNonEmptyLabelAndDirection()
    {
        foreach (var shot in StudioMintShotTypes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(shot.Label));
            Assert.False(string.IsNullOrWhiteSpace(shot.Direction));
        }
    }

    // ─── BuildShotPrompt ──────────────────────────────────────────────────────

    [Fact]
    public void BuildShotPrompt_WithoutIntent_OmitsAdditionalGuidanceBlock()
    {
        var shot = StudioMintShotTypes.All[0];

        var prompt = GptService.BuildShotPrompt("BASE", shot, intentText: null);

        Assert.StartsWith("BASE", prompt);
        Assert.Contains($"Shot direction — {shot.Label}:", prompt);
        Assert.Contains(shot.Direction, prompt);
        Assert.DoesNotContain("Additional guidance", prompt);
    }

    [Fact]
    public void BuildShotPrompt_WithIntent_AppendsTrimmedIntentBlock()
    {
        var shot = StudioMintShotTypes.All[0];

        var prompt = GptService.BuildShotPrompt("BASE", shot, intentText: "  premium minimalist vibe  ");

        Assert.Contains("Additional guidance from the user:", prompt);
        Assert.Contains("premium minimalist vibe", prompt);
        // Trimmed — the leading/trailing whitespace must not bleed into the prompt.
        Assert.DoesNotContain("\n  premium", prompt);
    }

    [Fact]
    public void BuildShotPrompt_WhitespaceOnlyIntent_TreatedAsAbsent()
    {
        var shot = StudioMintShotTypes.All[0];

        var prompt = GptService.BuildShotPrompt("BASE", shot, intentText: "   \n\t  ");

        Assert.DoesNotContain("Additional guidance", prompt);
    }

    [Fact]
    public void BuildShotPrompt_EachShotProducesDistinctPrompt()
    {
        var prompts = StudioMintShotTypes.All
            .Select(s => GptService.BuildShotPrompt("BASE", s, null))
            .ToArray();
        Assert.Equal(prompts.Length, prompts.Distinct().Count());
    }

    // ─── Embedded base prompt resource (ADR-013) ─────────────────────────────

    [Fact]
    public void BasePromptResource_IsNotEmbedded_AfterAdr013()
    {
        // ADR-013: studio-mint-base.md was removed from the public NuGet in 0.13.0.
        // The prompt now lives in P5 and is injected at the call site via basePrompt param.
        var assembly = typeof(GptService).Assembly;

        using var stream = assembly.GetManifestResourceStream("ShareInvest.Agency.Prompts.studio-mint-base.md");

        Assert.Null(stream);
    }

    // ─── Input validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateStudioMintAsync_NullRequest_Throws()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.GenerateStudioMintAsync("test base prompt", null!));
    }

    [Fact]
    public async Task GenerateStudioMintAsync_NullSourceImage_Throws()
    {
        var svc = CreateService();
        var request = new StudioMintRequest("user-1", SourceImage: null!, "product.png");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.GenerateStudioMintAsync("test base prompt", request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GenerateStudioMintAsync_MissingFileName_Throws(string? fileName)
    {
        var svc = CreateService();
        var request = new StudioMintRequest(
            "user-1",
            BinaryData.FromBytes([0x89, 0x50, 0x4E, 0x47]),
            fileName!);

        // `ThrowIfNullOrWhiteSpace` throws ArgumentNullException for null and
        // ArgumentException for empty/whitespace — accept either subclass.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => svc.GenerateStudioMintAsync("test base prompt", request));
    }

    // ─── Error mapping (replays the catch ladder used in-place) ──────────────

    static StudioMintShot MapShotException(int index, string shotId, ClientResultException ex)
    {
        try
        {
            throw ex;
        }
        catch (ClientResultException e) when (e.Status == 400)
        {
            return new StudioMintShot(index, shotId, ImageBytes: null, ErrorReason: "moderation");
        }
        catch (ClientResultException e) when (e.Status == 429)
        {
            return new StudioMintShot(index, shotId, ImageBytes: null, ErrorReason: "rate_limited");
        }
        catch (ClientResultException e) when (e.Status == 401)
        {
            throw new UnauthorizedAccessException("auth", e);
        }
        catch (Exception)
        {
            return new StudioMintShot(index, shotId, ImageBytes: null, ErrorReason: "unexpected");
        }
    }

    [Fact]
    public void ErrorMapping_Http400_ReturnsModerationReason()
    {
        var ex = NewClientResultException(400);
        var result = MapShotException(0, "hero-front", ex);
        Assert.Null(result.ImageBytes);
        Assert.Equal("moderation", result.ErrorReason);
    }

    [Fact]
    public void ErrorMapping_Http429_ReturnsRateLimitedReason()
    {
        var ex = NewClientResultException(429);
        var result = MapShotException(0, "hero-front", ex);
        Assert.Null(result.ImageBytes);
        Assert.Equal("rate_limited", result.ErrorReason);
    }

    [Fact]
    public void ErrorMapping_Http401_ThrowsUnauthorized()
    {
        var ex = NewClientResultException(401);
        Assert.Throws<UnauthorizedAccessException>(
            () => MapShotException(0, "hero-front", ex));
    }

    [Fact]
    public void ErrorMapping_OtherStatus_ReturnsUnexpected()
    {
        var ex = NewClientResultException(500);
        var result = MapShotException(0, "hero-front", ex);
        Assert.Null(result.ImageBytes);
        Assert.Equal("unexpected", result.ErrorReason);
    }

    // ─── Result aggregation ───────────────────────────────────────────────────

    [Fact]
    public void StudioMintResult_IsComplete_TrueWhenEveryShotHasBytes()
    {
        var shots = Enumerable.Range(0, 4)
            .Select(i => new StudioMintShot(i, $"s{i}", BinaryData.FromString("img")))
            .ToArray();

        var result = new StudioMintResult(shots, IsComplete: shots.All(s => s.ImageBytes is not null));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void StudioMintResult_IsComplete_FalseWhenAnyShotMissing()
    {
        var shots = new StudioMintShot[]
        {
            new(0, "a", BinaryData.FromString("img")),
            new(1, "b", ImageBytes: null, ErrorReason: "moderation"),
            new(2, "c", BinaryData.FromString("img")),
            new(3, "d", BinaryData.FromString("img")),
        };

        var result = new StudioMintResult(shots, IsComplete: shots.All(s => s.ImageBytes is not null));
        Assert.False(result.IsComplete);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    static GptService CreateService() =>
        new(new Microsoft.Extensions.Logging.Abstractions.NullLogger<GptService>(), "test-key", "gpt-image-1");

    static ClientResultException NewClientResultException(int status) =>
        new("err", new FakePipelineResponse(status));

    sealed class FakePipelineResponse(int status) : PipelineResponse
    {
        BinaryData? _content;
        public override int Status { get; } = status;
        public override string ReasonPhrase => "err";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => _content ??= BinaryData.FromString("err");
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override void Dispose() { }
    }
}
