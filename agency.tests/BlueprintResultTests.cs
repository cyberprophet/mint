using ShareInvest.Agency.Models;

using System.Text.Json;

namespace ShareInvest.Agency.Tests;

public class BlueprintResultTests
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    const string MinimalJson = """
        {
          "pageDesignSystem": {
            "mood": "fresh, sporty, clean",
            "brandColors": ["#FF5733", "#FFFFFF"],
            "backgroundApproach": "white studio",
            "typographyScale": "display-xl, body-md"
          },
          "visualBlocks": [],
          "assumptions": null
        }
        """;

    const string FullJson = """
        {
          "pageDesignSystem": {
            "mood": "premium, modern",
            "brandColors": ["#1A1A2E", "#E94560"],
            "backgroundApproach": "dark studio cutout with charcoal reflections",
            "typographyScale": "display-xl, body-md, highlights-sm"
          },
          "visualBlocks": [
            {
              "blockId": "block-01",
              "blockType": "hero",
              "sectionRefs": ["Hero"],
              "heightWeight": "xl",
              "layoutVariant": "split-50-50",
              "panels": [
                {
                  "role": "hero-product",
                  "heightRatio": 1.0,
                  "contentType": "copy-with-visual"
                }
              ],
              "assetSlots": [
                {
                  "slotId": "slot-01",
                  "prompt": "Premium sneaker on white studio background with dramatic side lighting, minimal shadows, hero angle, soft gradient, neutral palette, ample negative space",
                  "aspectRatio": "4:5",
                  "panelRef": "hero-product",
                  "priority": "high",
                  "negativeConstraints": ["no text", "no UI elements", "no buttons", "no captions"],
                  "imageUrl": null
                }
              ],
              "designOverrides": null
            }
          ],
          "assumptions": ["sectionType derived from strategicIntent", "hero layout inferred from product category"]
        }
        """;

    // ── Deserialization ───────────────────────────────────────────────────────

    [Fact]
    public void Deserializes_MinimalJson_WithEmptyBlocks()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(MinimalJson, Options);

        Assert.NotNull(result);
        Assert.NotNull(result.PageDesignSystem);
        Assert.Equal("fresh, sporty, clean", result.PageDesignSystem.Mood);
        Assert.Empty(result.VisualBlocks);
        Assert.Null(result.Assumptions);
    }

    [Fact]
    public void Deserializes_FullJson_WithVisualBlocks()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        Assert.Single(result.VisualBlocks);
    }

    [Fact]
    public void Deserializes_PageDesignSystem_Fields()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        var ds = result.PageDesignSystem;
        Assert.Equal("premium, modern", ds.Mood);
        Assert.Equal(["#1A1A2E", "#E94560"], ds.BrandColors);
        Assert.Equal("dark studio cutout with charcoal reflections", ds.BackgroundApproach);
        Assert.Equal("display-xl, body-md, highlights-sm", ds.TypographyScale);
    }

    [Fact]
    public void Deserializes_VisualBlock_Fields()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        var block = result.VisualBlocks[0];
        Assert.Equal("block-01", block.BlockId);
        Assert.Equal("hero", block.BlockType);
        Assert.Equal(["Hero"], block.SectionRefs);
        Assert.Equal("xl", block.HeightWeight);
        Assert.Equal("split-50-50", block.LayoutVariant);
        Assert.Null(block.DesignOverrides);
    }

    [Fact]
    public void Deserializes_LayoutPanel_Fields()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        var panel = result.VisualBlocks[0].Panels[0];
        Assert.Equal("hero-product", panel.Role);
        Assert.Equal(1.0, panel.HeightRatio);
        Assert.Equal("copy-with-visual", panel.ContentType);
    }

    [Fact]
    public void Deserializes_AssetSlot_Fields()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        var slot = result.VisualBlocks[0].AssetSlots[0];
        Assert.Equal("slot-01", slot.SlotId);
        Assert.Equal("4:5", slot.AspectRatio);
        Assert.Equal("hero-product", slot.PanelRef);
        Assert.Equal("high", slot.Priority);
        Assert.Null(slot.ImageUrl);
        Assert.Contains("no text", slot.NegativeConstraints);
        Assert.Contains("no UI elements", slot.NegativeConstraints);
        Assert.Contains("no buttons", slot.NegativeConstraints);
        Assert.Contains("no captions", slot.NegativeConstraints);
    }

    [Fact]
    public void Deserializes_Assumptions_WhenPresent()
    {
        var result = JsonSerializer.Deserialize<BlueprintResult>(FullJson, Options);

        Assert.NotNull(result);
        Assert.NotNull(result.Assumptions);
        Assert.Equal(2, result.Assumptions.Length);
        Assert.Contains("sectionType derived from strategicIntent", result.Assumptions);
    }

    // ── DesignOverrides ───────────────────────────────────────────────────────

    [Fact]
    public void Deserializes_DesignOverrides_WhenPresent()
    {
        var json = """
            {
              "pageDesignSystem": {
                "mood": "premium",
                "brandColors": ["#000"],
                "backgroundApproach": "dark",
                "typographyScale": "display-xl"
              },
              "visualBlocks": [
                {
                  "blockId": "block-02",
                  "blockType": "proof-trust",
                  "sectionRefs": ["Trust"],
                  "heightWeight": "medium",
                  "layoutVariant": "full-width",
                  "panels": [],
                  "assetSlots": [],
                  "designOverrides": {
                    "mood": "warm, approachable",
                    "backgroundApproach": null,
                    "typographyScale": null
                  }
                }
              ],
              "assumptions": null
            }
            """;

        var result = JsonSerializer.Deserialize<BlueprintResult>(json, Options);

        Assert.NotNull(result);
        var overrides = result.VisualBlocks[0].DesignOverrides;
        Assert.NotNull(overrides);
        Assert.Equal("warm, approachable", overrides.Mood);
        Assert.Null(overrides.BackgroundApproach);
        Assert.Null(overrides.TypographyScale);
    }

    // ── Constructor / record equality ─────────────────────────────────────────

    [Fact]
    public void Constructor_SetsAllFields()
    {
        var ds = new PageDesignSystem(
            Mood: "fresh",
            BrandColors: ["#FFF"],
            BackgroundApproach: "white",
            TypographyScale: "body-md");

        var block = new VisualBlock(
            BlockId: "b1",
            BlockType: "hero",
            SectionRefs: ["Hero"],
            HeightWeight: "xl",
            LayoutVariant: "split-50-50",
            Panels: [],
            AssetSlots: [],
            DesignOverrides: null);

        var blueprint = new BlueprintResult(ds, [block], null);

        Assert.Equal("fresh", blueprint.PageDesignSystem.Mood);
        Assert.Single(blueprint.VisualBlocks);
        Assert.Equal("b1", blueprint.VisualBlocks[0].BlockId);
        Assert.Null(blueprint.Assumptions);
    }

    [Fact]
    public void InvalidJson_Throws_JsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BlueprintResult>("not valid json", Options));
    }
}
