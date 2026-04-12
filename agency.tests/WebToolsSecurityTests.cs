namespace ShareInvest.Agency.Tests;

public class WebToolsSecurityTests : IDisposable
{
    readonly WebTools _webTools = new();

    [Theory]
    [InlineData("http://localhost/secret")]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    public async Task FetchAsync_BlocksInvalidSchemeAndLocalhost(string url)
    {
        // Localhost and non-http(s) schemes must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    public async Task FetchAsync_BlocksMalformedUrls(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void WebTools_Dispose_DoesNotThrow()
    {
        var tools = new WebTools();
        tools.Dispose(); // Must not throw
    }

    /// <summary>
    /// Verifies that a hostname which cannot be resolved via DNS is blocked rather than
    /// allowed through. This guards against DNS rebinding and resolution-failure SSRF bypasses.
    /// Before the fix, a catch block returned false (allow); it must now return true (block).
    /// </summary>
    [Theory]
    [InlineData("http://this-hostname-does-not-exist.invalid/path")]
    [InlineData("https://unresolvable-ssrf-test-host.example.invalid/")]
    public async Task FetchAsync_BlocksUnresolvableHostname(string url)
    {
        // An unresolvable hostname must be treated as private/blocked, not allowed through.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// S-07: Exa API key must never appear in the endpoint URL query string.
    /// The key must be transmitted only via the x-api-key request header.
    /// </summary>
    [Fact]
    public void WebTools_ExaApiKey_NotExposedInEndpointUrl()
    {
        // Use reflection to retrieve the private _exaEndpoint and _exaApiKey fields.
        var type = typeof(WebTools);
        var endpointField = type.GetField("_exaEndpoint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var keyField = type.GetField("_exaApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        const string testKey = "test-secret-key-12345";
        using var tools = new WebTools(testKey);

        var endpoint = (Uri?)endpointField!.GetValue(tools);
        var storedKey = (string?)keyField!.GetValue(tools);

        // The endpoint must not contain the API key in the URL (no query string leakage).
        Assert.NotNull(endpoint);
        Assert.DoesNotContain(testKey, endpoint.ToString());
        Assert.Null(endpoint.Query.Length > 0 ? endpoint.Query : null);

        // The key must be stored for header injection, not discarded.
        Assert.Equal(testKey, storedKey);
    }

    /// <summary>
    /// S-07: When no API key is provided, the endpoint URL must have no query string.
    /// </summary>
    [Fact]
    public void WebTools_NoApiKey_EndpointHasNoQueryString()
    {
        var type = typeof(WebTools);
        var endpointField = type.GetField("_exaEndpoint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        using var tools = new WebTools();
        var endpoint = (Uri?)endpointField!.GetValue(tools);

        Assert.NotNull(endpoint);
        Assert.True(string.IsNullOrEmpty(endpoint.Query), "Endpoint must have no query string when no API key is provided.");
    }

    public void Dispose() => _webTools.Dispose();
}
