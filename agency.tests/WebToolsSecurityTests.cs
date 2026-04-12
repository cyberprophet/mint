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

    public void Dispose() => _webTools.Dispose();
}
