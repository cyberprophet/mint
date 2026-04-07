namespace ShareInvest.Agency;

/// <summary>
/// Chains a primary and secondary <see cref="ISearchProvider"/>.
/// If the primary search throws any exception the secondary is tried instead.
/// </summary>
/// <param name="primary">The preferred search provider.</param>
/// <param name="secondary">The fallback search provider used when <paramref name="primary"/> fails.</param>
public class FallbackSearchProvider(ISearchProvider primary, ISearchProvider secondary) : ISearchProvider
{
    /// <inheritdoc />
    public async Task<string> SearchAsync(string query, int numResults = 8, CancellationToken ct = default)
    {
        try
        {
            return await primary.SearchAsync(query, numResults, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return await secondary.SearchAsync(query, numResults, ct);
        }
    }
}
