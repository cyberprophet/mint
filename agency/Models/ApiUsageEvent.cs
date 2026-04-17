namespace ShareInvest.Agency.Models;

/// <summary>
/// Reports token usage from an AI operation to the caller.
/// </summary>
/// <param name="Provider">API provider name (e.g., "openai").</param>
/// <param name="Model">Model name used for the operation.</param>
/// <param name="InputTokens">Number of input tokens consumed.</param>
/// <param name="OutputTokens">Number of output tokens generated.</param>
/// <param name="Purpose">Operation type: "title", "vision", "research", "image".</param>
/// <param name="MessageId">Optional message identifier for correlation (e.g. chat message ID).</param>
/// <param name="LatencyMs">Optional round-trip latency of the API call in milliseconds.</param>
/// <param name="RetryCount">Optional number of retries before a successful response (0 = first attempt succeeded).</param>
/// <param name="ImageQuality">For image models: "low", "medium", or "high". Null for text models.</param>
/// <param name="ImageSize">For image models: "1024x1024", "1024x1536", or "1536x1024". Null for text models.</param>
public record ApiUsageEvent(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    string Purpose,
    long? MessageId = null,
    int? LatencyMs = null,
    int? RetryCount = null,
    string? ImageQuality = null,
    string? ImageSize = null);
