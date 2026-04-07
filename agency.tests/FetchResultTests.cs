using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.Tests;

public class FetchResultTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var result = new FetchResult(
            FinalUrl: "https://example.com/product",
            StatusCode: 200,
            Title: "Example Product",
            MetaDescription: "A great product",
            OgImage: "https://example.com/img.jpg",
            JsonLd: """{"@type":"Product"}""",
            MainText: "This is the main content.",
            Warnings: null);

        Assert.Equal("https://example.com/product", result.FinalUrl);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Example Product", result.Title);
        Assert.Equal("A great product", result.MetaDescription);
        Assert.Equal("https://example.com/img.jpg", result.OgImage);
        Assert.Null(result.Warnings);
    }

    [Fact]
    public void ToPromptText_ContainsUrl()
    {
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: "Test",
            MetaDescription: null,
            OgImage: null,
            JsonLd: null,
            MainText: "Content here",
            Warnings: null);

        var text = result.ToPromptText();

        Assert.Contains("https://example.com", text);
        Assert.Contains("200", text);
        Assert.Contains("Content here", text);
    }

    [Fact]
    public void ToPromptText_IncludesAllMetadata_WhenPresent()
    {
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: "My Title",
            MetaDescription: "My Description",
            OgImage: "https://example.com/og.jpg",
            JsonLd: null,
            MainText: "Body",
            Warnings: null);

        var text = result.ToPromptText();

        Assert.Contains("My Title", text);
        Assert.Contains("My Description", text);
        Assert.Contains("https://example.com/og.jpg", text);
    }

    [Fact]
    public void ToPromptText_OmitsNullFields()
    {
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: null,
            MetaDescription: null,
            OgImage: null,
            JsonLd: null,
            MainText: "Body",
            Warnings: null);

        var text = result.ToPromptText();

        Assert.DoesNotContain("Title:", text);
        Assert.DoesNotContain("Description:", text);
        Assert.DoesNotContain("OG Image:", text);
    }

    [Fact]
    public void Warnings_CanBeCollectionExpression()
    {
        var result = new FetchResult(
            FinalUrl: "https://example.com",
            StatusCode: 200,
            Title: null,
            MetaDescription: null,
            OgImage: null,
            JsonLd: null,
            MainText: "",
            Warnings: ["Content truncated to 8,000 characters"]);

        Assert.NotNull(result.Warnings);
        Assert.Single(result.Warnings);
    }
}
