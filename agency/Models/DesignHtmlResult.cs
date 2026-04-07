namespace ShareInvest.Agency.Models;

/// <summary>
/// Result of Athena HTML design generation via OpenAI tool-calling loop.
/// </summary>
public record DesignHtmlResult(string Html, int TokensUsed, int Attempts);
