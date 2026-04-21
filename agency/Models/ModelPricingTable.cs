namespace ShareInvest.Agency.Models;

/// <summary>Per-model token pricing used to estimate API costs.</summary>
/// <param name="InputUsdPer1M">Input token cost per 1M tokens (USD).</param>
/// <param name="OutputUsdPer1M">Output token cost per 1M tokens (USD).</param>
/// <param name="CacheWriteUsdPer1M">Cache creation cost per 1M tokens (USD). Zero if not applicable.</param>
/// <param name="CacheReadUsdPer1M">Cache read cost per 1M tokens (USD). Zero if not applicable.</param>
public record ModelPricing(decimal InputUsdPer1M, decimal OutputUsdPer1M, decimal CacheWriteUsdPer1M = 0, decimal CacheReadUsdPer1M = 0);

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
            // Scope: text-prompt-to-image generation only. `InputUsdPer1M`
            // is OpenAI's **text input** rate, not the image-input rate
            // (which applies to source-image tokens when using image edit
            // / inpainting endpoints). P5's current flow is prompt-only —
            // if image editing is wired up later, add separate buckets.
            // Output = generated image tokens; count varies by quality × size.
            [("openai", "gpt-image-1")]      = new(5.00m, 40.00m),
            [("openai", "gpt-image-1.5")]    = new(8.00m, 32.00m),
            [("openai", "gpt-image-1-mini")] = new(2.50m, 8.00m),
        }.AsReadOnly();

    /// <summary>Estimates the USD cost for a single API call based on token counts. Returns null for unknown models.</summary>
    public static decimal? EstimateCost(string provider, string model, int inputTokens, int outputTokens, int? cacheWriteTokens = null, int? cacheReadTokens = null)
    {
        if (!Prices.TryGetValue((provider, model), out var pricing))
            return null;

        var cost = (inputTokens / 1_000_000m * pricing.InputUsdPer1M)
                 + (outputTokens / 1_000_000m * pricing.OutputUsdPer1M);

        if (cacheWriteTokens.HasValue)
            cost += cacheWriteTokens.Value / 1_000_000m * pricing.CacheWriteUsdPer1M;
        if (cacheReadTokens.HasValue)
            cost += cacheReadTokens.Value / 1_000_000m * pricing.CacheReadUsdPer1M;

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

        return EstimateCost(usage.Provider, usage.Model, usage.InputTokens, usage.OutputTokens);
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
