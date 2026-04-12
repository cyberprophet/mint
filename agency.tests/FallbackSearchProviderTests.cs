using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests for <see cref="FallbackSearchProvider"/>.
/// Verifies the primary→secondary fallback chain under success, failure, and cancellation scenarios.
/// </summary>
public class FallbackSearchProviderTests
{
    readonly ISearchProvider _primary = Substitute.For<ISearchProvider>();
    readonly ISearchProvider _secondary = Substitute.For<ISearchProvider>();
    readonly FallbackSearchProvider _sut;

    public FallbackSearchProviderTests()
    {
        _sut = new FallbackSearchProvider(_primary, _secondary);
    }

    // ─── Primary succeeds ─────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_PrimarySucceeds_ReturnsPrimaryResult()
    {
        _primary.SearchAsync("query", 8, Arg.Any<CancellationToken>())
                .Returns("primary result");

        var result = await _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken);

        Assert.Equal("primary result", result);
    }

    [Fact]
    public async Task SearchAsync_PrimarySucceeds_SecondaryIsNeverCalled()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns("ok");

        await _sut.SearchAsync("something", ct: TestContext.Current.CancellationToken);

        await _secondary.DidNotReceive()
                        .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_PassesQueryAndNumResults_ToPrimary()
    {
        _primary.SearchAsync("my query", 5, Arg.Any<CancellationToken>())
                .Returns("result");

        await _sut.SearchAsync("my query", 5, TestContext.Current.CancellationToken);

        await _primary.Received(1)
                      .SearchAsync("my query", 5, Arg.Any<CancellationToken>());
    }

    // ─── Primary throws, fallback to secondary ────────────────────────────────

    [Fact]
    public async Task SearchAsync_PrimaryThrowsHttpException_FallsBackToSecondary()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("network error"));

        _secondary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                  .Returns("secondary result");

        var result = await _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken);

        Assert.Equal("secondary result", result);
    }

    [Fact]
    public async Task SearchAsync_PrimaryThrowsGenericException_FallsBackToSecondary()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("provider unavailable"));

        _secondary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                  .Returns("fallback result");

        var result = await _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken);

        Assert.Equal("fallback result", result);
    }

    [Fact]
    public async Task SearchAsync_PrimaryThrows_QueryIsForwardedToSecondary()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("boom"));

        _secondary.SearchAsync("specific query", 3, Arg.Any<CancellationToken>())
                  .Returns("ok");

        await _sut.SearchAsync("specific query", 3, TestContext.Current.CancellationToken);

        await _secondary.Received(1)
                        .SearchAsync("specific query", 3, Arg.Any<CancellationToken>());
    }

    // ─── Both providers throw ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_BothThrow_ExceptionFromSecondaryPropagates()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("primary failed"));

        _secondary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                  .ThrowsAsync(new InvalidOperationException("secondary failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_BothThrow_SecondaryExceptionMessageIsPreserved()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("primary down"));

        _secondary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                  .ThrowsAsync(new HttpRequestException("secondary unreachable"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken));

        Assert.Contains("secondary unreachable", ex.Message);
    }

    // ─── Cancellation behavior ────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_CancellationRequested_IsNotSwallowedAsFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new OperationCanceledException(cts.Token));

        // OperationCanceledException must propagate directly — not treated as a fallback trigger.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.SearchAsync("query", ct: cts.Token));
    }

    [Fact]
    public async Task SearchAsync_CancellationRequested_SecondaryIsNeverCalled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.SearchAsync("query", ct: cts.Token));

        await _secondary.DidNotReceive()
                        .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ─── Result format validation ─────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_PrimaryReturnsEmptyString_ReturnsEmptyString()
    {
        // An empty string from the primary is a valid result — do not treat as failure.
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(string.Empty);

        var result = await _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, result);
        await _secondary.DidNotReceive()
                        .SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_SecondaryReturnsMultilineResult_IsReturnedAsIs()
    {
        _primary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new Exception("offline"));

        const string multiline = "Result 1\nResult 2\nResult 3";
        _secondary.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                  .Returns(multiline);

        var result = await _sut.SearchAsync("query", ct: TestContext.Current.CancellationToken);

        Assert.Equal(multiline, result);
    }

    [Fact]
    public async Task SearchAsync_DefaultNumResults_Is8()
    {
        _primary.SearchAsync("q", 8, Arg.Any<CancellationToken>())
                .Returns("ok");

        // Call without explicit numResults — default must be 8.
        await _sut.SearchAsync("q", ct: TestContext.Current.CancellationToken);

        await _primary.Received(1)
                      .SearchAsync("q", 8, Arg.Any<CancellationToken>());
    }
}
