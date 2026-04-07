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
}
