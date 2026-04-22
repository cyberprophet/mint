using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.Tests;

public class ApiUsageEventTests
{
    [Fact]
    public void Constructor_WithRequiredOnly_HasNullOptionals()
    {
        var evt = new ApiUsageEvent("openai", "gpt-5-nano", 100, 50, "title");

        Assert.Equal("openai", evt.Provider);
        Assert.Equal("gpt-5-nano", evt.Model);
        Assert.Equal(100, evt.InputTokens);
        Assert.Equal(50, evt.OutputTokens);
        Assert.Equal("title", evt.Purpose);
        Assert.Null(evt.MessageId);
        Assert.Null(evt.LatencyMs);
        Assert.Null(evt.RetryCount);
    }

    [Fact]
    public void Constructor_WithLatencyMs_SetsField()
    {
        var evt = new ApiUsageEvent("openai", "gpt-5.4", 200, 100, "vision", LatencyMs: 1500);

        Assert.Equal(1500, evt.LatencyMs);
        Assert.Null(evt.RetryCount);
    }

    [Fact]
    public void Constructor_WithRetryCount_SetsField()
    {
        var evt = new ApiUsageEvent("openai", "gpt-5.4-nano", 300, 150, "research", RetryCount: 3);

        Assert.Equal(3, evt.RetryCount);
        Assert.Null(evt.LatencyMs);
    }

    [Fact]
    public void Constructor_WithAllFields_SetsAll()
    {
        var evt = new ApiUsageEvent("openai", "gpt-image-1", 0, 0, "image",
            MessageId: 42L, LatencyMs: 5000, RetryCount: 1);

        Assert.Equal(42L, evt.MessageId);
        Assert.Equal(5000, evt.LatencyMs);
        Assert.Equal(1, evt.RetryCount);
    }

    [Fact]
    public void Record_Equality_WorksOnAllFields()
    {
        var a = new ApiUsageEvent("openai", "gpt-5-nano", 10, 5, "title", LatencyMs: 100);
        var b = new ApiUsageEvent("openai", "gpt-5-nano", 10, 5, "title", LatencyMs: 100);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_Inequality_WhenLatencyDiffers()
    {
        var a = new ApiUsageEvent("openai", "gpt-5-nano", 10, 5, "title", LatencyMs: 100);
        var b = new ApiUsageEvent("openai", "gpt-5-nano", 10, 5, "title", LatencyMs: 200);

        Assert.NotEqual(a, b);
    }

    // ─── Image-pricing fields (added for ModelPricingTable v4) ────────────────

    [Fact]
    public void Constructor_TextModel_ImageFieldsDefaultNull()
    {
        var evt = new ApiUsageEvent("openai", "gpt-5.4", 100, 50, "blueprint");

        Assert.Null(evt.ImageQuality);
        Assert.Null(evt.ImageSize);
        Assert.Null(evt.ImageInputTokens);
        Assert.Null(evt.ImageCacheReadTokens);
    }

    [Fact]
    public void Constructor_ImageModel_AllImageFieldsSet()
    {
        var evt = new ApiUsageEvent(
            "openai", "gpt-image-1", 200, 4160, "image",
            LatencyMs: 12000,
            ImageQuality: "high",
            ImageSize: "1024x1024",
            ImageInputTokens: 1568,
            ImageCacheReadTokens: 400);

        Assert.Equal("high", evt.ImageQuality);
        Assert.Equal("1024x1024", evt.ImageSize);
        Assert.Equal(1568, evt.ImageInputTokens);
        Assert.Equal(400, evt.ImageCacheReadTokens);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Constructor_AllImageQualityStrings_RoundTrip(string quality)
    {
        var evt = new ApiUsageEvent("openai", "gpt-image-1", 0, 0, "image", ImageQuality: quality);

        Assert.Equal(quality, evt.ImageQuality);
    }

    [Theory]
    [InlineData("1024x1024")]
    [InlineData("1024x1536")]
    [InlineData("1536x1024")]
    public void Constructor_AllImageSizeStrings_RoundTrip(string size)
    {
        var evt = new ApiUsageEvent("openai", "gpt-image-1", 0, 0, "image", ImageSize: size);

        Assert.Equal(size, evt.ImageSize);
    }

    [Fact]
    public void Record_Inequality_WhenImageInputTokensDiffer()
    {
        var a = new ApiUsageEvent("openai", "gpt-image-1", 100, 4000, "image", ImageInputTokens: 1000);
        var b = new ApiUsageEvent("openai", "gpt-image-1", 100, 4000, "image", ImageInputTokens: 2000);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Record_Equality_WhenAllFieldsMatch_IncludingImageFields()
    {
        var a = new ApiUsageEvent(
            "openai", "gpt-image-1.5", 200, 4000, "image",
            LatencyMs: 5000, ImageQuality: "high", ImageSize: "1024x1024",
            ImageInputTokens: 1500, ImageCacheReadTokens: 200);
        var b = new ApiUsageEvent(
            "openai", "gpt-image-1.5", 200, 4000, "image",
            LatencyMs: 5000, ImageQuality: "high", ImageSize: "1024x1024",
            ImageInputTokens: 1500, ImageCacheReadTokens: 200);

        Assert.Equal(a, b);
    }
}
