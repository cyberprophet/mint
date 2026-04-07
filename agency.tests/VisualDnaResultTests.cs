using ShareInvest.Agency.Models;

using System.Text.Json;

namespace ShareInvest.Agency.Tests;

public class VisualDnaResultTests
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserializes_ValidJson()
    {
        var json = """
            {
              "dominantColors": ["#FFFFFF", "#000000"],
              "mood": "premium",
              "materials": ["glass", "matte"],
              "style": "luxury-minimal",
              "backgroundType": "white-studio",
              "rawDescription": "A premium product on white background."
            }
            """;

        var result = JsonSerializer.Deserialize<VisualDnaResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal("premium", result.Mood);
        Assert.Equal("luxury-minimal", result.Style);
        Assert.Equal("white-studio", result.BackgroundType);
    }

    [Fact]
    public void Normalize_KnownLabels_ReturnsSameLower()
    {
        var result = new VisualDnaResult(
            DominantColors: ["#FFF"],
            Mood: "Premium",
            Materials: ["glass"],
            Style: "Luxury-Minimal",
            BackgroundType: "White-Studio",
            RawDescription: "Test");

        var normalized = result.Normalize();

        Assert.Equal("premium", normalized.Mood);
        Assert.Equal("luxury-minimal", normalized.Style);
        Assert.Equal("white-studio", normalized.BackgroundType);
    }

    [Fact]
    public void Normalize_UnknownLabel_MapsToUnknown()
    {
        var result = new VisualDnaResult(
            DominantColors: [],
            Mood: "futuristic",
            Materials: [],
            Style: "alien-tech",
            BackgroundType: "holographic",
            RawDescription: "Test");

        var normalized = result.Normalize();

        Assert.Equal("unknown", normalized.Mood);
        Assert.Equal("unknown", normalized.Style);
        Assert.Equal("unknown", normalized.BackgroundType);
    }

    [Fact]
    public void Normalize_Issue17Labels_AreAllowed()
    {
        // Verify new labels from issue #17 are accepted
        var result = new VisualDnaResult(
            DominantColors: [],
            Mood: "casual",
            Materials: [],
            Style: "handcrafted",
            BackgroundType: "abstract",
            RawDescription: "Test");

        var normalized = result.Normalize();

        Assert.Equal("casual", normalized.Mood);
        Assert.Equal("handcrafted", normalized.Style);
        Assert.Equal("abstract", normalized.BackgroundType);
    }

    [Fact]
    public void AllowedMoods_ContainsExpectedValues()
    {
        Assert.Contains("premium", VisualDnaResult.AllowedMoods);
        Assert.Contains("casual", VisualDnaResult.AllowedMoods);
        Assert.Contains("unknown", VisualDnaResult.AllowedMoods);
    }

    [Fact]
    public void AllowedStyles_ContainsExpectedValues()
    {
        Assert.Contains("luxury-minimal", VisualDnaResult.AllowedStyles);
        Assert.Contains("handcrafted", VisualDnaResult.AllowedStyles);
        Assert.Contains("unknown", VisualDnaResult.AllowedStyles);
    }
}
