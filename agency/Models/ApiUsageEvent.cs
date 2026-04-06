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
public record ApiUsageEvent(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    string Purpose,
    long? MessageId = null);
