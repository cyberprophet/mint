namespace ShareInvest.Agency;

/// <summary>
/// Abstraction for web search providers used by the research engine.
/// Implementations include <see cref="WebTools"/> (Exa MCP) and
/// <see cref="FallbackSearchProvider"/> (primary → secondary chain).
/// </summary>
public interface ISearchProvider
{
    /// <summary>
    /// Searches the web for the given query and returns results as text.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="numResults">Number of results to return (default: 8).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Search results as plain text.</returns>
    Task<string> SearchAsync(string query, int numResults = 8, CancellationToken ct = default);
}
