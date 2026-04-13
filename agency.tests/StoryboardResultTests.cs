using ShareInvest.Agency.Models;

using System.Text.Json;

namespace ShareInvest.Agency.Tests;

public class StoryboardResultTests
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    const string MinimalJson = """
        {
          "sections": [],
          "ctaText": "Buy Now"
        }
        """;

    const string FullJson = """
        {
          "sections": [
            {
              "title": "Hero",
              "strategicIntent": "Capture attention and establish brand promise",
              "sectionType": "hero",
              "blocks": [
                { "type": "heading", "content": "Run Faster. Feel Lighter." },
                { "type": "text", "content": "Engineered for elite performance." }
              ]
            },
            {
              "title": "Problem Awareness",
              "strategicIntent": "Identify the runner's core pain point",
              "sectionType": null,
              "blocks": [
                { "type": "heading", "content": "Most shoes slow you down." },
                { "type": "highlight", "content": "Proprietary foam absorbs 30% more impact." }
              ]
            }
          ],
          "ctaText": "Shop the Collection"
        }
        """;

    // ── Deserialization ───────────────────────────────────────────────────────

    [Fact]
    public void Deserializes_MinimalJson_WithEmptySections()
    {
        var result = JsonSerializer.Deserialize<StoryboardResult>(MinimalJson, Options);

        Assert.NotNull(result);
        Assert.Empty(result.Sections);
        Assert.Equal("Buy Now", result.CtaText);
    }

    [Fact]
    public void Deserializes_FullJson_WithTwoSections()
    {
        var result = JsonSerializer.Deserialize<StoryboardResult>(FullJson, Options);

        Assert.NotNull(result);
        Assert.Equal(2, result.Sections.Length);
        Assert.Equal("Shop the Collection", result.CtaText);
    }

    [Fact]
    public void Deserializes_HeroSection_Fields()
    {
        var result = JsonSerializer.Deserialize<StoryboardResult>(FullJson, Options);

        Assert.NotNull(result);
        var hero = result.Sections[0];
        Assert.Equal("Hero", hero.Title);
        Assert.Equal("Capture attention and establish brand promise", hero.StrategicIntent);
        Assert.Equal("hero", hero.SectionType);
        Assert.Equal(2, hero.Blocks.Length);
    }

    [Fact]
    public void Deserializes_Section_WithNullSectionType()
    {
        var result = JsonSerializer.Deserialize<StoryboardResult>(FullJson, Options);

        Assert.NotNull(result);
        var section = result.Sections[1];
        Assert.Equal("Problem Awareness", section.Title);
        Assert.Null(section.SectionType);
    }

    [Fact]
    public void Deserializes_StoryboardBlock_Fields()
    {
        var result = JsonSerializer.Deserialize<StoryboardResult>(FullJson, Options);

        Assert.NotNull(result);
        var blocks = result.Sections[0].Blocks;
        Assert.Equal("heading", blocks[0].Type);
        Assert.Equal("Run Faster. Feel Lighter.", blocks[0].Content);
        Assert.Equal("text", blocks[1].Type);
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("text")]
    [InlineData("highlight")]
    public void Deserializes_AllKnownBlockTypes(string blockType)
    {
        var json = $$"""
            {
              "sections": [
                {
                  "title": "Section",
                  "strategicIntent": "Test",
                  "sectionType": null,
                  "blocks": [
                    { "type": "{{blockType}}", "content": "Some content." }
                  ]
                }
              ],
              "ctaText": "Go"
            }
            """;

        var result = JsonSerializer.Deserialize<StoryboardResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(blockType, result.Sections[0].Blocks[0].Type);
    }

    // ── Constructor / record equality ─────────────────────────────────────────

    [Fact]
    public void Constructor_SetsAllFields()
    {
        var block = new StoryboardBlock(Type: "heading", Content: "Hello World");
        var section = new StoryboardSection(
            Title: "Hero",
            StrategicIntent: "Capture attention",
            SectionType: "hero",
            Blocks: [block]);

        var storyboard = new StoryboardResult(Sections: [section], CtaText: "Order Now");

        Assert.Single(storyboard.Sections);
        Assert.Equal("Hero", storyboard.Sections[0].Title);
        Assert.Equal("hero", storyboard.Sections[0].SectionType);
        Assert.Single(storyboard.Sections[0].Blocks);
        Assert.Equal("heading", storyboard.Sections[0].Blocks[0].Type);
        Assert.Equal("Order Now", storyboard.CtaText);
    }

    [Fact]
    public void InvalidJson_Throws_JsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<StoryboardResult>("not valid json", Options));
    }

    [Fact]
    public void Deserializes_MultipleBlocks_InSingleSection()
    {
        var json = """
            {
              "sections": [
                {
                  "title": "Trust Signals",
                  "strategicIntent": "Build credibility",
                  "sectionType": "proof-trust",
                  "blocks": [
                    { "type": "heading", "content": "Trusted by 10,000 runners." },
                    { "type": "highlight", "content": "4.9 stars from 3,200 reviews." },
                    { "type": "image", "content": "A collage of happy athletes wearing the shoe in natural outdoor settings, warm sunlight, candid energy, authentic lifestyle mood" },
                    { "type": "text", "content": "Free returns within 30 days." }
                  ]
                }
              ],
              "ctaText": "Try Risk-Free"
            }
            """;

        var result = JsonSerializer.Deserialize<StoryboardResult>(json, Options);

        Assert.NotNull(result);
        Assert.Equal(4, result.Sections[0].Blocks.Length);
        Assert.Equal("Try Risk-Free", result.CtaText);
    }
}
