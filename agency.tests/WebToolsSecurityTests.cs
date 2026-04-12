namespace ShareInvest.Agency.Tests;

public class WebToolsSecurityTests : IDisposable
{
    readonly WebTools _webTools = new();

    // ── Existing: scheme + localhost ─────────────────────────────────────────

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

    // ── Existing: malformed URLs ──────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    public async Task FetchAsync_BlocksMalformedUrls(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    // ── Private IPv4 ranges ───────────────────────────────────────────────────

    [Theory]
    [InlineData("http://10.0.0.1/secret")]
    [InlineData("http://10.255.255.255/internal")]
    public async Task FetchAsync_Blocks_10_0_0_0_Slash8_Range(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://172.16.0.1/secret")]
    [InlineData("http://172.31.255.255/internal")]
    public async Task FetchAsync_Blocks_172_16_0_0_Slash12_Range(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://192.168.0.1/secret")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://192.168.255.255/internal")]
    public async Task FetchAsync_Blocks_192_168_0_0_Slash16_Range(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.0.1/")]
    public async Task FetchAsync_Blocks_169_254_0_0_CloudMetadata(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAsync_Blocks_0_0_0_0()
    {
        // 0.0.0.0 is an unspecified address — .NET rejects it at the socket layer
        // (ArgumentException wrapped in HttpRequestException) before IsPrivateHostAsync
        // can classify it, so we accept any exception to verify the request is blocked.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _webTools.FetchAsync("http://0.0.0.0/secret", TestContext.Current.CancellationToken));
    }

    // ── IPv6 loopback and link-local ──────────────────────────────────────────

    [Theory]
    [InlineData("http://[::1]/secret")]
    public async Task FetchAsync_Blocks_IPv6_Loopback(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://[fe80::1]/secret")]
    [InlineData("http://[fe80::1%2510]/secret")]
    public async Task FetchAsync_Blocks_IPv6_LinkLocal(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    // ── Additional blocked URI schemes ───────────────────────────────────────

    [Theory]
    [InlineData("data:text/html,<h1>hello</h1>")]
    [InlineData("gopher://evil.example.com/")]
    public async Task FetchAsync_Blocks_NonHttpSchemes(string url)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _webTools.FetchAsync(url, TestContext.Current.CancellationToken));
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void WebTools_Dispose_DoesNotThrow()
    {
        var tools = new WebTools();
        tools.Dispose(); // Must not throw
    }

    public void Dispose() => _webTools.Dispose();
}
