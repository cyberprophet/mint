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
/// </summary>
public static class ModelPricingTable
{
    /// <summary>Increment when pricing entries are added, removed, or changed.</summary>
    public const int PricingVersion = 1;

    /// <summary>Known prices keyed by (provider, model) tuple.</summary>
    public static readonly IReadOnlyDictionary<(string Provider, string Model), ModelPricing> Prices = new Dictionary<(string, string), ModelPricing>
    {
        [("openai", "gpt-5.4")] = new(1.25m, 10.00m),
        [("openai", "gpt-5.4-nano")] = new(0.05m, 0.40m),
        [("openai", "gpt-5-nano")] = new(0.05m, 0.40m),
        [("openai", "gpt-image-1")] = new(10.00m, 40.00m),
        [("anthropic", "claude-haiku-4-5-20251001")] = new(1.00m, 5.00m, 1.25m, 0.10m),
    };

    /// <summary>Estimates the USD cost for a single API call based on token counts.</summary>
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
}
