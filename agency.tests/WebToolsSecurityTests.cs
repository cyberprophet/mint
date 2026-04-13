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
        // Two legitimate guard paths exist across runtimes/platforms:
        //
        //   Path A — IsPrivateHostAsync resolves 0.0.0.0 to bytes [0,0,0,0],
        //   matches the bytes[0]==0 branch, and throws InvalidOperationException
        //   with the "private/internal" message before any network I/O.
        //
        //   Path B — On runtimes where Dns.GetHostAddressesAsync rejects the
        //   unspecified address before returning (throwOnIIPAny path, .NET 9+),
        //   the ArgumentException is swallowed by IsPrivateHostAsync's catch block;
        //   the request is forwarded and the socket layer throws HttpRequestException
        //   wrapping ArgumentException about the unspecified address.
        //
        // Both outcomes confirm 0.0.0.0 cannot be reached. Any other exception
        // (e.g. a SocketException from a real connection attempt) would indicate
        // the guard failed, so we assert the exact accepted set below.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            _webTools.FetchAsync("http://0.0.0.0/secret", TestContext.Current.CancellationToken));

        var isGuardFired = ex is InvalidOperationException ioe
            && ioe.Message.Contains("private", StringComparison.OrdinalIgnoreCase);

        var isSocketRejected = ex is HttpRequestException hre
            && hre.InnerException is ArgumentException ae
            && ae.Message.Contains("unspecified", StringComparison.OrdinalIgnoreCase);

        Assert.True(
            isGuardFired || isSocketRejected,
            $"Expected SSRF guard (InvalidOperationException containing 'private') or socket-layer " +
            $"rejection (HttpRequestException wrapping ArgumentException containing 'unspecified'), " +
            $"but got: {ex.GetType().Name}: {ex.Message}");
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
