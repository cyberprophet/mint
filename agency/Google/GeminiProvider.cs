using Google.GenAI;
using Google.GenAI.Types;

using Microsoft.Extensions.Logging;

using ShareInvest.Agency.Models;

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShareInvest.Agency.Google;

/// <summary>
/// Google Gemini provider implementing text generation and vision capabilities.
/// Image generation via Imagen is deferred to a future phase.
/// </summary>
public partial class GeminiProvider : ITextGenerationProvider, IVisionProvider
{
    /// <summary>
    /// Initializes a new instance of <see cref="GeminiProvider"/>.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output.</param>
    /// <param name="apiKey">Google AI API key.</param>
    public GeminiProvider(ILogger<GeminiProvider> logger, string apiKey)
    {
        this.logger = logger;
        client = new Client(apiKey: apiKey);
    }

    /// <inheritdoc />
    public string ProviderName => "gemini";

    readonly ILogger<GeminiProvider> logger;
    readonly Client client;

    /// <inheritdoc />
    public virtual async Task<string?> GenerateTitleAsync(
        string systemPrompt,
        string conversationText,
        string model = "gemini-2.5-flash",
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var config = new GenerateContentConfig
        {
            SystemInstruction = TextContent(systemPrompt),
            MaxOutputTokens = 1024,
            Temperature = 0.3f,
        };

        var sw = Stopwatch.StartNew();
        var response = await client.Models.GenerateContentAsync(
            model: model,
            contents: TextContent($"<conversation>\n{conversationText}\n</conversation>"),
            config: config,
            cancellationToken: cancellationToken);
        sw.Stop();

        ReportUsage(response, model, "title", sw.ElapsedMilliseconds, onUsage);

        var raw = response.Text;

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return CleanTitleResponse(raw);
    }

    /// <inheritdoc />
    public virtual async Task<VisualDnaResult?> AnalyzeImageAsync(
        string systemPrompt,
        BinaryData imageBytes,
        string mimeType,
        string model = "gemini-2.5-flash",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var config = new GenerateContentConfig
        {
            SystemInstruction = TextContent(systemPrompt),
            MaxOutputTokens = 2048,
            Temperature = 0.2f,
        };

        var content = new Content
        {
            Parts =
            [
                new Part
                {
                    InlineData = new Blob
                    {
                        MimeType = mimeType,
                        Data = imageBytes.ToArray(),
                    },
                },
                new Part { Text = "Extract Visual DNA from this product image. Return JSON only." },
            ],
        };

        var sw = Stopwatch.StartNew();
        var response = await client.Models.GenerateContentAsync(
            model: model,
            contents: content,
            config: config,
            cancellationToken: cancellationToken);
        sw.Stop();

        ReportUsage(response, model, "vision", sw.ElapsedMilliseconds, onUsage);

        var raw = response.Text;

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return TryParseVisualDna(raw);
    }

    /// <inheritdoc />
    public Task<StoryboardResult?> GenerateStoryboardAsync(
        string systemPrompt, StoryboardContext context, string model,
        CancellationToken cancellationToken = default, Action<ApiUsageEvent>? onUsage = null)
        => throw new NotSupportedException("Storyboard generation via Gemini requires tool-calling migration (ADR-014 Phase 3).");

    /// <inheritdoc />
    public Task<BlueprintResult?> GenerateBlueprintAsync(
        string systemPrompt, BlueprintContext context, string model,
        CancellationToken cancellationToken = default, Action<ApiUsageEvent>? onUsage = null)
        => throw new NotSupportedException("Blueprint generation via Gemini requires tool-calling migration (ADR-014 Phase 3).");

    /// <inheritdoc />
    public Task<DesignHtmlResult?> GenerateDesignHtmlAsync(
        string systemPrompt, DesignHtmlContext context, string model,
        CancellationToken cancellationToken = default, Action<ApiUsageEvent>? onUsage = null)
        => throw new NotSupportedException("Design HTML generation via Gemini requires tool-calling migration (ADR-014 Phase 3).");

    /// <inheritdoc />
    public Task<ResearchResult?> ResearchProductAsync(
        string systemPrompt, string productInfo, string[] urls, string? category, string model,
        CancellationToken cancellationToken = default, Action<ApiUsageEvent>? onUsage = null)
        => throw new NotSupportedException("Research via Gemini requires tool-calling migration (ADR-014 Phase 3).");

    /// <inheritdoc />
    public virtual async Task<ProductInfoResult?> ExtractProductInfoAsync(
        string systemPrompt,
        IReadOnlyList<ProductInfoDocument> documents,
        string model = "gemini-2.5-flash",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
            return null;

        var documentBlock = string.Join("\n\n", documents.Select(d =>
            $"--- DOCUMENT: {d.Id} ---\n{d.Text}\n--- END ---"));

        var config = new GenerateContentConfig
        {
            SystemInstruction = TextContent(systemPrompt),
            MaxOutputTokens = 4096,
            Temperature = 0.1f,
        };

        var sw = Stopwatch.StartNew();
        var response = await client.Models.GenerateContentAsync(
            model: model,
            contents: TextContent(documentBlock),
            config: config,
            cancellationToken: cancellationToken);
        sw.Stop();

        ReportUsage(response, model, "product_info", sw.ElapsedMilliseconds, onUsage);

        var raw = response.Text;

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return TryParseProductInfo(raw, documents);
    }

    /// <inheritdoc />
    public virtual async Task<ReferenceLinkAnalysis?> AnalyzeReferenceLinkAsync(
        string systemPrompt,
        string url,
        string html,
        ReferenceLinkContext context,
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);
        ArgumentNullException.ThrowIfNull(context);

        var truncatedHtml = html.Length > 12_000 ? html[..12_000] + "\n[…truncated]" : html;

        var userMessage = $"""
            Analyze this reference page for design and copy DNA.

            URL: {url}
            Target language: {context.TargetLanguage}
            {(context.ProductName is not null ? $"Product context: {context.ProductName}" : "")}

            --- PAGE HTML (truncated) ---
            {truncatedHtml}
            --- END HTML ---

            Return JSON with fields: layoutPattern, copyTone, colorPalette (hex array), typographyStyle, messagingAngles (string array), rawSummary.
            """;

        var config = new GenerateContentConfig
        {
            SystemInstruction = TextContent(systemPrompt),
            MaxOutputTokens = 2048,
            Temperature = 0.2f,
        };

        // ITextGenerationProvider.AnalyzeReferenceLinkAsync does not expose a model
        // parameter — the interface delegates model selection to the provider.
        const string refLinkModel = "gemini-2.5-flash";

        var sw = Stopwatch.StartNew();
        var response = await client.Models.GenerateContentAsync(
            model: refLinkModel,
            contents: TextContent(userMessage),
            config: config,
            cancellationToken: cancellationToken);
        sw.Stop();

        ReportUsage(response, refLinkModel, "reference_link", sw.ElapsedMilliseconds, onUsage);

        var raw = response.Text;

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return TryParseReferenceLinkAnalysis(raw);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Google.GenAI.Client does not implement IDisposable as of v1.6.1.
        (client as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────

    static Content TextContent(string text) => new()
    {
        Parts = [new Part { Text = text }],
    };

    void ReportUsage(GenerateContentResponse response, string model, string purpose, long latencyMs, Action<ApiUsageEvent>? onUsage)
    {
        if (onUsage is null || response.UsageMetadata is not { } usage)
            return;

        onUsage(new ApiUsageEvent(
            ProviderName, model,
            usage.PromptTokenCount ?? 0,
            usage.CandidatesTokenCount ?? 0,
            purpose,
            LatencyMs: (int)latencyMs));
    }

    static string? CleanTitleResponse(string raw)
    {
        var cleaned = ThinkBlockRegex().Replace(raw, string.Empty);
        var title = cleaned
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('"'))
            .FirstOrDefault(l => l.Length > 0);

        if (title is null) return null;
        if (title.Length > 50) title = string.Concat(title.AsSpan(0, 47), "...");
        return title;
    }

    static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    VisualDnaResult? TryParseVisualDna(string raw)
    {
        var json = ExtractJsonBlock(raw);
        if (json is null) return null;

        try
        {
            return JsonSerializer.Deserialize<VisualDnaResult>(json, CaseInsensitiveOptions)?.Normalize();
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Gemini visual-dna response");
            return null;
        }
    }

    ProductInfoResult? TryParseProductInfo(string raw, IReadOnlyList<ProductInfoDocument> documents)
    {
        var json = ExtractJsonBlock(raw);
        if (json is null) return null;

        try
        {
            var result = JsonSerializer.Deserialize<ProductInfoResult>(json, CaseInsensitiveOptions);
            if (result is null) return null;

            // Normalize provenance: filter SourceDocuments to only known input IDs
            var knownIds = new HashSet<string>(documents.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            var validSources = result.SourceDocuments?
                .Where(s => knownIds.Contains(s))
                .ToArray();

            return result with { SourceDocuments = validSources };
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Gemini product-info response");
            return null;
        }
    }

    ReferenceLinkAnalysis? TryParseReferenceLinkAnalysis(string raw)
    {
        var json = ExtractJsonBlock(raw);
        if (json is null) return null;

        try
        {
            return JsonSerializer.Deserialize<ReferenceLinkAnalysis>(json, CaseInsensitiveOptions);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Gemini reference-link analysis response");
            return null;
        }
    }

    static string? ExtractJsonBlock(string raw)
    {
        var fenceMatch = JsonFenceRegex().Match(raw);
        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');

        if (start >= 0 && end > start)
            return raw[start..(end + 1)];

        return null;
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>\s*")]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex(@"```(?:json)?\s*\n([\s\S]*?)\n\s*```")]
    private static partial Regex JsonFenceRegex();
}
