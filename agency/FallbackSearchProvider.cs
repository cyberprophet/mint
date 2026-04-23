using Microsoft.Extensions.Logging;

namespace ShareInvest.Agency;

/// <summary>
/// Chains a primary and secondary <see cref="ISearchProvider"/>.
/// If the primary search throws any non-cancellation exception the secondary is tried instead,
/// and the failure is logged as a warning so operational issues remain visible.
/// </summary>
/// <param name="primary">The preferred search provider.</param>
/// <param name="secondary">The fallback search provider used when <paramref name="primary"/> fails.</param>
/// <param name="logger">Logger for recording primary provider failures.</param>
public class FallbackSearchProvider(
    ISearchProvider primary,
    ISearchProvider secondary,
    ILogger<FallbackSearchProvider> logger) : ISearchProvider
{
    /// <inheritdoc />
    public async Task<string> SearchAsync(string query, int numResults = 8, CancellationToken ct = default)
    {
        try
        {
            return await primary.SearchAsync(query, numResults, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Primary search provider {Provider} failed; falling back to secondary",
                primary.GetType().Name);

            return await secondary.SearchAsync(query, numResults, ct).ConfigureAwait(false);
        }
    }
}
