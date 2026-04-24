using ShareInvest.Agency.Models;

namespace Agency.Tests;

public class ModelPricingTableTests
{
    [Theory]
    [InlineData("openai", "gpt-5.4", 1_000_000, 1_000_000, 2.50 + 15.00)]
    [InlineData("openai", "gpt-5.4-nano", 1_000_000, 1_000_000, 0.20 + 1.25)]
    [InlineData("openai", "gpt-5-nano", 1_000_000, 1_000_000, 0.05 + 0.40)]
    [InlineData("anthropic", "claude-haiku-4-5-20251001", 1_000_000, 1_000_000, 1.00 + 5.00)]
    public void EstimateCost_TextModels_MatchesProviderRates(string provider, string model, int input, int output, double expected)
    {
        var cost = ModelPricingTable.EstimateCost(provider, model, input, output);

        Assert.NotNull(cost);
        Assert.Equal((decimal)expected, cost.Value);
    }

    [Theory]
    // Text-only input (pure generation). InputUsdPer1M applies; ImageInput is 0.
    [InlineData("openai", "gpt-image-1", 1_000_000, 1_000_000, 5.00 + 40.00)]
    [InlineData("openai", "gpt-image-1.5", 1_000_000, 1_000_000, 5.00 + 32.00)]
    [InlineData("openai", "gpt-image-1-mini", 1_000_000, 1_000_000, 2.00 + 8.00)]
    // gpt-image-2 (mint#99) — estimated at gpt-image-1.5 rates until OpenAI
    // publishes a distinct pricing row.
    [InlineData("openai", "gpt-image-2", 1_000_000, 1_000_000, 5.00 + 32.00)]
    public void EstimateCost_ImageModels_TextOnly_UsesTextInputRate(string provider, string model, int input, int output, double expected)
    {
        var cost = ModelPricingTable.EstimateCost(provider, model, input, output);

        Assert.NotNull(cost);
        Assert.Equal((decimal)expected, cost.Value);
    }

    [Theory]
    // Image-edit call (StudioMint): all three buckets filled. Rates multiply
    // each token count by its respective per-1M rate.
    // gpt-image-1:      1M text × $5  + 1M image × $10 + 1M out × $40 = $55
    // gpt-image-1.5:    1M text × $5  + 1M image × $8  + 1M out × $32 = $45
    // gpt-image-1-mini: 1M text × $2  + 1M image × $2.5 + 1M out × $8 = $12.5
    [InlineData("openai", "gpt-image-1", 1_000_000, 1_000_000, 1_000_000, 5.00 + 10.00 + 40.00)]
    [InlineData("openai", "gpt-image-1.5", 1_000_000, 1_000_000, 1_000_000, 5.00 + 8.00 + 32.00)]
    [InlineData("openai", "gpt-image-1-mini", 1_000_000, 1_000_000, 1_000_000, 2.00 + 2.50 + 8.00)]
    // gpt-image-2 shares gpt-image-1.5's rates (mint#99).
    [InlineData("openai", "gpt-image-2", 1_000_000, 1_000_000, 1_000_000, 5.00 + 8.00 + 32.00)]
    public void EstimateCost_ImageModels_WithSourceImage_UsesBothInputRates(
        string provider, string model, int textInput, int imageInput, int output, double expected)
    {
        var cost = ModelPricingTable.EstimateCost(
            provider, model, textInput, output,
            imageInputTokens: imageInput);

        Assert.NotNull(cost);
        Assert.Equal((decimal)expected, cost.Value);
    }

    /// <summary>
    /// Verify that token-based image pricing produces values consistent with
    /// OpenAI's published per-image prices (precomputed from tokens).
    /// </summary>
    [Fact]
    public void EstimateCost_ImageGeneration_MatchesPerImagePrice()
    {
        // High-quality 1024×1024 pure generation:
        //   ~4,160 output tokens, ~200 text prompt tokens, 0 source-image tokens.
        var cost = ModelPricingTable.EstimateCost("openai", "gpt-image-1",
            inputTokens: 200, outputTokens: 4_160);

        Assert.NotNull(cost);

        // Text input: 200/1M × $5 = $0.00100
        // Output:     4160/1M × $40 = $0.16640
        // Total: $0.16740
        Assert.Equal(0.16740m, cost.Value);
    }

    [Fact]
    public void EstimateCost_ImageEdit_IncludesImageInputAtHigherRate()
    {
        // StudioMint-style edit call: 1024×1024 source image → ~1,568 image
        // input tokens, plus a ~100-token text prompt, and a ~4,160-token
        // generated output.
        var cost = ModelPricingTable.EstimateCost(
            "openai", "gpt-image-1",
            inputTokens: 100,
            outputTokens: 4_160,
            imageInputTokens: 1_568);

        Assert.NotNull(cost);

        // Text:  100  / 1M × $5  = $0.0005
        // Image: 1568 / 1M × $10 = $0.01568
        // Out:   4160 / 1M × $40 = $0.16640
        // Total: $0.18258
        Assert.Equal(0.18258m, cost.Value);
    }

    [Fact]
    public void EstimateCost_UnknownModel_ReturnsNull()
    {
        var cost = ModelPricingTable.EstimateCost("openai", "unknown-model", 1000, 1000);

        Assert.Null(cost);
    }

    [Fact]
    public void EstimateCost_CaseInsensitive()
    {
        var lower = ModelPricingTable.EstimateCost("openai", "gpt-image-1", 1000, 1000);
        var upper = ModelPricingTable.EstimateCost("OpenAI", "GPT-IMAGE-1", 1000, 1000);

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void EstimateCost_ApiUsageEvent_RoutesCorrectly()
    {
        var textEvent = new ApiUsageEvent("openai", "gpt-5.4", 5000, 8000, "blueprint");
        var imageEvent = new ApiUsageEvent("openai", "gpt-image-1", 200, 4160, "image",
            ImageQuality: "high", ImageSize: "1024x1024");

        var textCost = ModelPricingTable.EstimateCost(textEvent);
        var imageCost = ModelPricingTable.EstimateCost(imageEvent);

        Assert.NotNull(textCost);
        Assert.NotNull(imageCost);

        // Text: 5000/1M × 2.50 + 8000/1M × 15.00 = 0.0125 + 0.12 = 0.1325
        Assert.Equal(0.1325m, textCost.Value);

        // Text input: 200/1M × $5 + 4160/1M × $40 = 0.001 + 0.1664 = 0.1674
        // (No source image in this event — uses text-input rate only.)
        Assert.Equal(0.1674m, imageCost.Value);
    }

    [Fact]
    public void EstimateCost_Anthropic_IncludesCacheTokens()
    {
        var cost = ModelPricingTable.EstimateCost("anthropic", "claude-haiku-4-5-20251001",
            inputTokens: 1_000_000, outputTokens: 500_000,
            cacheWriteTokens: 200_000, cacheReadTokens: 800_000);

        Assert.NotNull(cost);

        // Input:      1M/1M × 1.00 = 1.00
        // Output:   500K/1M × 5.00 = 2.50
        // CacheWrite: 200K/1M × 1.25 = 0.25
        // CacheRead:  800K/1M × 0.10 = 0.08
        // Total: 3.83
        Assert.Equal(3.83m, cost.Value);
    }

    [Fact]
    public void EstimateCost_ImageModel_ZeroOutputTokens_ReturnsNull()
    {
        // When OpenAI SDK returns null Usage, tokens default to 0.
        // Fail-closed: return null (unknown cost) instead of $0.
        var usage = new ApiUsageEvent("openai", "gpt-image-1", 0, 0, "image",
            ImageQuality: "high", ImageSize: "1024x1024");

        Assert.Null(ModelPricingTable.EstimateCost(usage));
    }

    [Fact]
    public void EstimateCost_TextModel_ZeroTokens_ReturnsZero()
    {
        // Text models with 0 tokens should still return 0 (not null) — this is valid.
        var usage = new ApiUsageEvent("openai", "gpt-5.4", 0, 0, "blueprint");

        var cost = ModelPricingTable.EstimateCost(usage);

        Assert.NotNull(cost);
        Assert.Equal(0m, cost.Value);
    }

    [Fact]
    public void PricingVersion_IsFive()
    {
        // Bumped to 5 in 0.16.7 when gpt-image-2 was added (mint#99).
        Assert.Equal(5, ModelPricingTable.PricingVersion);
    }
}
