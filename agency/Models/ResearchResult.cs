namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured research output produced by the librarian research engine for a product.
/// </summary>
/// <param name="ProductData">Metadata extracted from each provided product URL.</param>
/// <param name="CompetitorInsights">Competitor products and positioning found during market research.</param>
/// <param name="MarketContext">Summary of category trends, demand signals, and market dynamics.</param>
/// <param name="SynthesizedInsights">Combined narrative of product strengths and market opportunities.</param>
/// <param name="Category">Primary product category (e.g., "Anti-aging skincare").</param>
/// <param name="CoreValue">Single-sentence core value proposition.</param>
/// <param name="KeySellingPoints">Concrete selling points supported by research (3–6 items).</param>
/// <param name="RecommendedAngle">Recommended marketing angle or narrative for a landing page.</param>
public record ResearchResult(
    ProductData[] ProductData,
    CompetitorInsight[] CompetitorInsights,
    string? MarketContext,
    string? SynthesizedInsights,
    string? Category,
    string? CoreValue,
    string[]? KeySellingPoints,
    string? RecommendedAngle);

/// <summary>
/// Metadata extracted from a single product page URL.
/// </summary>
/// <param name="SourceUrl">The URL that was fetched.</param>
/// <param name="Title">Page title or product name.</param>
/// <param name="Description">Product description or meta description.</param>
/// <param name="Price">Price string if publicly visible (e.g., "$29.99").</param>
/// <param name="Brand">Brand or manufacturer name.</param>
/// <param name="Features">Key feature or benefit strings.</param>
/// <param name="OgImage">og:image URL if present.</param>
/// <param name="SchemaType">schema.org type if present (e.g., "Product", "ItemPage").</param>
public record ProductData(
    string SourceUrl,
    string? Title,
    string? Description,
    string? Price,
    string? Brand,
    string[]? Features,
    string? OgImage,
    string? SchemaType);

/// <summary>
/// Competitor product or brand identified during market research.
/// </summary>
/// <param name="Name">Competitor product or brand name.</param>
/// <param name="Url">Competitor URL.</param>
/// <param name="Positioning">How the competitor positions themselves in the market.</param>
/// <param name="PriceRange">Price range if available.</param>
/// <param name="VisualStyle">Visual or aesthetic style (e.g., "clinical white", "premium dark").</param>
/// <param name="Differentiators">Key selling points or differentiators the competitor emphasizes.</param>
public record CompetitorInsight(
    string Name,
    string Url,
    string Positioning,
    string? PriceRange,
    string? VisualStyle,
    string[]? Differentiators);
