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
    [InlineData("openai", "gpt-image-1", 1_000_000, 1_000_000, 10.00 + 40.00)]
    [InlineData("openai", "gpt-image-1.5", 1_000_000, 1_000_000, 8.00 + 32.00)]
    [InlineData("openai", "gpt-image-1-mini", 1_000_000, 1_000_000, 2.50 + 8.00)]
    public void EstimateCost_ImageModels_UsesImageModalityTokenRates(string provider, string model, int input, int output, double expected)
    {
        var cost = ModelPricingTable.EstimateCost(provider, model, input, output);

        Assert.NotNull(cost);
        Assert.Equal((decimal)expected, cost.Value);
    }

    /// <summary>
    /// Verify that token-based image pricing produces values consistent with
    /// OpenAI's published per-image prices (which are precomputed from tokens).
    /// high quality 1024×1024 ≈ 4,160 output tokens → 4160/1M × $40 = $0.1664.
    /// </summary>
    [Fact]
    public void EstimateCost_ImageGeneration_MatchesPerImagePrice()
    {
        // high quality 1024×1024: ~4,160 output tokens, ~200 text input tokens
        var cost = ModelPricingTable.EstimateCost("openai", "gpt-image-1",
            inputTokens: 200, outputTokens: 4_160);

        Assert.NotNull(cost);

        // Output: 4160/1M × $40 = $0.16640
        // Input:  200/1M × $10 = $0.00200
        // Total: $0.16840
        Assert.Equal(0.16840m, cost.Value);
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

        // Image: 200/1M × 10.00 + 4160/1M × 40.00 = 0.002 + 0.1664 = 0.1684
        Assert.Equal(0.1684m, imageCost.Value);
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
    public void PricingVersion_IsThree()
    {
        Assert.Equal(3, ModelPricingTable.PricingVersion);
    }
}
