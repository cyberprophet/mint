namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured result returned by <see cref="ShareInvest.Agency.WebTools.FetchAsync"/>.
/// Separates page metadata from main text content instead of concatenating everything into a flat string.
/// </summary>
/// <param name="FinalUrl">The final URL after any redirects.</param>
/// <param name="StatusCode">HTTP status code of the final response.</param>
/// <param name="Title">Page &lt;title&gt; or og:title if present.</param>
/// <param name="MetaDescription">Content of the meta description or og:description tag.</param>
/// <param name="OgImage">og:image URL if present.</param>
/// <param name="JsonLd">Raw JSON-LD structured data block(s) if present.</param>
/// <param name="MainText">Extracted plain-text body content (max 8,000 characters).</param>
/// <param name="Warnings">Optional warnings generated during fetch (e.g., Cloudflare retry, content truncated).</param>
public record FetchResult(
    string FinalUrl,
    int StatusCode,
    string? Title,
    string? MetaDescription,
    string? OgImage,
    string? JsonLd,
    string MainText,
    string[]? Warnings)
{
    /// <summary>
    /// Formats this result as a GPT-friendly text block for use in research prompts.
    /// </summary>
    public string ToPromptText()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"URL: {FinalUrl} ({StatusCode})");

        if (!string.IsNullOrWhiteSpace(Title))
            sb.AppendLine($"Title: {Title}");

        if (!string.IsNullOrWhiteSpace(MetaDescription))
            sb.AppendLine($"Description: {MetaDescription}");

        if (!string.IsNullOrWhiteSpace(OgImage))
            sb.AppendLine($"OG Image: {OgImage}");

        sb.AppendLine("---");
        sb.Append(MainText);

        return sb.ToString();
    }
}
