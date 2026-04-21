namespace ShareInvest.Agency.Models;

/// <summary>Per-model token pricing used to estimate API costs.</summary>
/// <param name="InputUsdPer1M">Text-input token cost per 1M tokens (USD). For image models this is OpenAI's text-input rate, for text models it's the only input rate.</param>
/// <param name="OutputUsdPer1M">Output token cost per 1M tokens (USD).</param>
/// <param name="CacheWriteUsdPer1M">Cache creation cost per 1M tokens (USD). Zero if not applicable (Anthropic-only).</param>
/// <param name="CacheReadUsdPer1M">Cached text-input cost per 1M tokens (USD). Zero if not applicable.</param>
/// <param name="ImageInputUsdPer1M">Image-input (source-image) token cost per 1M tokens (USD). Null for text models. Applies to image edits / inpainting calls where OpenAI bills source-image tokens at a higher rate than text prompt tokens.</param>
/// <param name="ImageCacheReadUsdPer1M">Cached image-input cost per 1M tokens (USD). Null for text models.</param>
public record ModelPricing(
    decimal InputUsdPer1M,
    decimal OutputUsdPer1M,
    decimal CacheWriteUsdPer1M = 0,
    decimal CacheReadUsdPer1M = 0,
    decimal? ImageInputUsdPer1M = null,
    decimal? ImageCacheReadUsdPer1M = null);

/// <summary>
/// Static lookup table of provider+model token prices. Prices are sourced
/// from each provider's public pricing page and updated manually.
/// Bump <see cref="PricingVersion"/> when entries change.
///
/// Image generation models (gpt-image-*) use the Image modality token rates.
/// The OpenAI API reports input_tokens and output_tokens for image calls;
/// output tokens represent the generated image and scale with quality × size
/// (e.g. high 1024×1024 ≈ 4,160 output tokens).
/// </summary>
public static class ModelPricingTable
{
    /// <summary>Increment when pricing entries are added, removed, or changed.</summary>
    public const int PricingVersion = 4;

    /// <summary>
    /// Known model prices keyed by (provider, model) tuple. Lookups are case-insensitive.
    /// Image models use Image modality rates (verified 2026-04-21 against OpenAI pricing page).
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Provider, string Model), ModelPricing> Prices =
        new Dictionary<(string, string), ModelPricing>(ProviderModelComparer.Instance)
        {
            // --- Text models ---
            [("openai", "gpt-5.4")]      = new(2.50m, 15.00m),
            [("openai", "gpt-5.4-nano")] = new(0.20m, 1.25m),
            [("openai", "gpt-5-nano")]   = new(0.05m, 0.40m),

            [("anthropic", "claude-haiku-4-5-20251001")] = new(1.00m, 5.00m, 1.25m, 0.10m),

            // --- Image models (Image modality token rates) ---
            //
            // OpenAI bills image models with TWO separate input rates:
            //   - Text input: tokens from the text prompt (and any cached
            //     text inputs).
            //   - Image input: tokens from source images passed to
            //     image-edit / inpainting endpoints.
            //
            // The OpenAI SDK reports the two counts separately via
            // `ImageInputTokenUsageDetails.TextTokens` and `.ImageTokens`
            // on `ImageTokenUsage.InputTokenDetails`. Call sites (e.g.
            // GptService.StudioMint.GenerateImageEditsAsync,
            // GptService.Image.GenerateImagesAsync) split the counts
            // when populating `ApiUsageEvent.InputTokens` (text portion)
            // and `ApiUsageEvent.ImageInputTokens` (image portion).
            //
            // Rates verified 2026-04-21 against OpenAI's published table:
            //   gpt-image-1       text $5.00 / img $10.00 / out $40.00 / cached-text $1.25 / cached-img $2.50
            //   gpt-image-1.5     text $5.00 / img $8.00  / out $32.00 / cached-text $1.25 / cached-img $2.00
            //   gpt-image-1-mini  text $2.00 / img $2.50  / out $8.00  / cached-text $0.20 / cached-img $0.25
            [("openai", "gpt-image-1")]      = new(5.00m, 40.00m, CacheReadUsdPer1M: 1.25m, ImageInputUsdPer1M: 10.00m, ImageCacheReadUsdPer1M: 2.50m),
            [("openai", "gpt-image-1.5")]    = new(5.00m, 32.00m, CacheReadUsdPer1M: 1.25m, ImageInputUsdPer1M: 8.00m,  ImageCacheReadUsdPer1M: 2.00m),
            [("openai", "gpt-image-1-mini")] = new(2.00m, 8.00m,  CacheReadUsdPer1M: 0.20m, ImageInputUsdPer1M: 2.50m, ImageCacheReadUsdPer1M: 0.25m),
        }.AsReadOnly();

    /// <summary>Estimates the USD cost for a single API call based on token counts. Returns null for unknown models.</summary>
    /// <param name="inputTokens">Text-input tokens (prompt tokens). For image models this is the <c>TextTokens</c> portion of <c>ImageInputTokenUsageDetails</c>.</param>
    /// <param name="outputTokens">Output tokens (generated image tokens for image models; completion tokens for text models).</param>
    /// <param name="cacheWriteTokens">Anthropic-only cache-creation tokens.</param>
    /// <param name="cacheReadTokens">Cached text-input tokens (Anthropic cache read, OpenAI "cached input").</param>
    /// <param name="imageInputTokens">Source-image input tokens on image-edit calls (the <c>ImageTokens</c> portion of <c>ImageInputTokenUsageDetails</c>). Billed at <see cref="ModelPricing.ImageInputUsdPer1M"/>. Null/zero for text-only flows.</param>
    /// <param name="imageCacheReadTokens">Cached image-input tokens. Billed at <see cref="ModelPricing.ImageCacheReadUsdPer1M"/>.</param>
    public static decimal? EstimateCost(
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        int? cacheWriteTokens = null,
        int? cacheReadTokens = null,
        int? imageInputTokens = null,
        int? imageCacheReadTokens = null)
    {
        if (!Prices.TryGetValue((provider, model), out var pricing))
            return null;

        var cost = (inputTokens / 1_000_000m * pricing.InputUsdPer1M)
                 + (outputTokens / 1_000_000m * pricing.OutputUsdPer1M);

        if (cacheWriteTokens.HasValue)
            cost += cacheWriteTokens.Value / 1_000_000m * pricing.CacheWriteUsdPer1M;
        if (cacheReadTokens.HasValue)
            cost += cacheReadTokens.Value / 1_000_000m * pricing.CacheReadUsdPer1M;
        if (imageInputTokens.HasValue && pricing.ImageInputUsdPer1M is { } imageInputRate)
            cost += imageInputTokens.Value / 1_000_000m * imageInputRate;
        if (imageCacheReadTokens.HasValue && pricing.ImageCacheReadUsdPer1M is { } imageCacheRate)
            cost += imageCacheReadTokens.Value / 1_000_000m * imageCacheRate;

        return cost;
    }

    /// <summary>
    /// Unified estimator: resolves provider+model from <see cref="ApiUsageEvent"/> and delegates to the token-based calculator.
    /// Returns null (fail-closed) when an image model reports zero output tokens, which indicates the API did not return usage data.
    /// </summary>
    public static decimal? EstimateCost(ApiUsageEvent usage)
    {
        if (usage.OutputTokens == 0
            && usage.Model.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase))
            return null;

        return EstimateCost(
            usage.Provider,
            usage.Model,
            usage.InputTokens,
            usage.OutputTokens,
            imageInputTokens: usage.ImageInputTokens);
    }

    sealed class ProviderModelComparer : IEqualityComparer<(string, string)>
    {
        public static readonly ProviderModelComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
