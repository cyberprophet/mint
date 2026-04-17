namespace ShareInvest.Agency.Models;

/// <summary>Per-model token pricing used to estimate API costs.</summary>
/// <param name="InputUsdPer1M">Input token cost per 1M tokens (USD).</param>
/// <param name="OutputUsdPer1M">Output token cost per 1M tokens (USD).</param>
/// <param name="CacheWriteUsdPer1M">Cache creation cost per 1M tokens (USD). Zero if not applicable.</param>
/// <param name="CacheReadUsdPer1M">Cache read cost per 1M tokens (USD). Zero if not applicable.</param>
public record ModelPricing(decimal InputUsdPer1M, decimal OutputUsdPer1M, decimal CacheWriteUsdPer1M = 0, decimal CacheReadUsdPer1M = 0);

/// <summary>Per-image pricing keyed by (quality, size) for image generation models.</summary>
/// <param name="CostPerImage">USD cost per generated image.</param>
public record ImagePricing(decimal CostPerImage);

/// <summary>
/// Static lookup table of provider+model token prices. Prices are sourced
/// from each provider's public pricing page and updated manually.
/// Bump <see cref="PricingVersion"/> when entries change.
/// </summary>
public static class ModelPricingTable
{
    /// <summary>Increment when pricing entries are added, removed, or changed.</summary>
    public const int PricingVersion = 2;

    /// <summary>Known text-model prices keyed by (provider, model) tuple. Lookups are case-insensitive.</summary>
    public static readonly IReadOnlyDictionary<(string Provider, string Model), ModelPricing> Prices =
        new Dictionary<(string, string), ModelPricing>(ProviderModelComparer.Instance)
        {
            [("openai", "gpt-5.4")] = new(2.50m, 15.00m),
            [("openai", "gpt-5.4-nano")] = new(0.20m, 1.25m),
            [("openai", "gpt-5-nano")] = new(0.05m, 0.40m),
            [("anthropic", "claude-haiku-4-5-20251001")] = new(1.00m, 5.00m, 1.25m, 0.10m),
        }.AsReadOnly();

    /// <summary>Per-image prices keyed by (model, quality, size). Verified 2026-04-17.</summary>
    public static readonly IReadOnlyDictionary<(string Model, string Quality, string Size), ImagePricing> ImagePrices =
        new Dictionary<(string, string, string), ImagePricing>(ImageKeyComparer.Instance)
        {
        [("gpt-image-1", "low", "1024x1024")] = new(0.011m),
        [("gpt-image-1", "low", "1024x1536")] = new(0.016m),
        [("gpt-image-1", "low", "1536x1024")] = new(0.016m),
        [("gpt-image-1", "medium", "1024x1024")] = new(0.042m),
        [("gpt-image-1", "medium", "1024x1536")] = new(0.063m),
        [("gpt-image-1", "medium", "1536x1024")] = new(0.063m),
        [("gpt-image-1", "high", "1024x1024")] = new(0.167m),
        [("gpt-image-1", "high", "1024x1536")] = new(0.25m),
        [("gpt-image-1", "high", "1536x1024")] = new(0.25m),

        [("gpt-image-1.5", "low", "1024x1024")] = new(0.009m),
        [("gpt-image-1.5", "low", "1024x1536")] = new(0.013m),
        [("gpt-image-1.5", "low", "1536x1024")] = new(0.013m),
        [("gpt-image-1.5", "medium", "1024x1024")] = new(0.034m),
        [("gpt-image-1.5", "medium", "1024x1536")] = new(0.05m),
        [("gpt-image-1.5", "medium", "1536x1024")] = new(0.05m),
        [("gpt-image-1.5", "high", "1024x1024")] = new(0.133m),
        [("gpt-image-1.5", "high", "1024x1536")] = new(0.2m),
        [("gpt-image-1.5", "high", "1536x1024")] = new(0.2m),
    }.AsReadOnly();

    /// <summary>Estimates the USD cost for a single text-model API call based on token counts. Returns null for unknown models.</summary>
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

    /// <summary>Estimates the USD cost for a single image generation call. Returns null for unknown model/quality/size combinations.</summary>
    public static decimal? EstimateImageCost(string model, string? quality, string? size)
    {
        quality ??= "high";
        size ??= "1024x1024";

        return ImagePrices.TryGetValue((model, quality, size), out var pricing)
            ? pricing.CostPerImage
            : null;
    }

    /// <summary>Unified estimator: routes to <see cref="EstimateCost"/> for text models or <see cref="EstimateImageCost"/> for image models based on <see cref="ApiUsageEvent"/>.</summary>
    public static decimal? EstimateCost(ApiUsageEvent usage)
    {
        if (usage.ImageQuality is not null || usage.ImageSize is not null)
            return EstimateImageCost(usage.Model, usage.ImageQuality, usage.ImageSize);

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

    sealed class ImageKeyComparer : IEqualityComparer<(string, string, string)>
    {
        public static readonly ImageKeyComparer Instance = new();

        public bool Equals((string, string, string) x, (string, string, string) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Item3, y.Item3);

        public int GetHashCode((string, string, string) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item3));
    }
}
