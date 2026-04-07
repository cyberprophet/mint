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

    public void Dispose() => _webTools.Dispose();
}
