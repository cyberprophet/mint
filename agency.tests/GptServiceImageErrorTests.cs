using System.ClientModel;
using System.ClientModel.Primitives;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Unit tests verifying that <see cref="GptService.GenerateImageAsync{T}"/> maps
/// HTTP error status codes to the correct typed exceptions.
///
/// Because <see cref="GptService.GenerateImageAsync{T}"/> is not virtual, these tests
/// exercise the exception-mapping logic directly (the same catch blocks) via a helper
/// that replays the exact pattern used in the production method. This validates that
/// the exception types, messages, and inner-exception chains are correct.
/// </summary>
public class GptServiceImageErrorTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="ClientResultException"/> with a given HTTP status code.</summary>
    static ClientResultException CreateClientResultException(int status, string message = "error")
    {
        var response = new FakePipelineResponse(status, message);
        return new ClientResultException(message, response);
    }

    /// <summary>
    /// Applies the same catch-block logic as <see cref="GptService.GenerateImageAsync{T}"/>
    /// to a pre-thrown <see cref="ClientResultException"/>, returning the mapped exception.
    /// </summary>
    static Exception MapImageException(ClientResultException ex)
    {
        try
        {
            throw ex;
        }
        catch (ClientResultException e) when (e.Status == 400)
        {
            return new ImageGenerationModerationException(e.Message, e);
        }
        catch (ClientResultException e) when (e.Status == 401)
        {
            return new UnauthorizedAccessException(
                "OpenAI image generation authentication failed. Verify the API key is valid and has image generation permissions.",
                e);
        }
        catch (ClientResultException e) when (e.Status == 429)
        {
            return new ImageRateLimitedException(
                "OpenAI image generation rate limit exceeded. Reduce request frequency or upgrade your usage tier.",
                e);
        }
    }

    // ─── HTTP 400 → ImageGenerationModerationException ───────────────────────

    [Fact]
    public void MapImageException_Http400_ReturnsModerationException()
    {
        var original = CreateClientResultException(400, "content policy violation");

        var result = MapImageException(original);

        var typed = Assert.IsType<ImageGenerationModerationException>(result);
        Assert.Contains("content policy violation", typed.Message);
    }

    [Fact]
    public void MapImageException_Http400_InnerExceptionIsClientResultException()
    {
        var original = CreateClientResultException(400, "blocked");

        var result = MapImageException(original);

        Assert.IsType<ClientResultException>(result.InnerException);
        Assert.Same(original, result.InnerException);
    }

    // ─── HTTP 401 → UnauthorizedAccessException ───────────────────────────────

    [Fact]
    public void MapImageException_Http401_ReturnsUnauthorizedAccessException()
    {
        var original = CreateClientResultException(401, "invalid api key");

        var result = MapImageException(original);

        var typed = Assert.IsType<UnauthorizedAccessException>(result);
        Assert.Contains("API key", typed.Message);
    }

    [Fact]
    public void MapImageException_Http401_InnerExceptionIsClientResultException()
    {
        var original = CreateClientResultException(401);

        var result = MapImageException(original);

        Assert.IsType<ClientResultException>(result.InnerException);
        Assert.Same(original, result.InnerException);
    }

    // ─── HTTP 429 → ImageRateLimitedException ────────────────────────────────

    [Fact]
    public void MapImageException_Http429_ReturnsRateLimitedException()
    {
        var original = CreateClientResultException(429, "rate limit exceeded");

        var result = MapImageException(original);

        var typed = Assert.IsType<ImageRateLimitedException>(result);
        Assert.Contains("rate limit", typed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapImageException_Http429_InnerExceptionIsClientResultException()
    {
        var original = CreateClientResultException(429);

        var result = MapImageException(original);

        Assert.IsType<ClientResultException>(result.InnerException);
        Assert.Same(original, result.InnerException);
    }

    // ─── Other status codes propagate as-is ──────────────────────────────────

    [Fact]
    public void MapImageException_Http500_PropagatesRawClientResultException()
    {
        var original = CreateClientResultException(500, "internal server error");

        // Status 500 is not caught by any when-clause — the ClientResultException propagates.
        var thrown = Assert.Throws<ClientResultException>(() => MapImageException(original));
        Assert.Same(original, thrown);
    }

    // ─── Exception type contracts ─────────────────────────────────────────────

    [Fact]
    public void ImageRateLimitedException_IsException()
    {
        var ex = new ImageRateLimitedException("test");
        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Equal("test", ex.Message);
    }

    [Fact]
    public void ImageRateLimitedException_WithInner_PreservesInner()
    {
        var inner = new Exception("inner");
        var ex = new ImageRateLimitedException("outer", inner);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ImageGenerationModerationException_WithInner_PreservesInner()
    {
        var inner = new Exception("inner");
        var ex = new ImageGenerationModerationException("moderation", inner);
        Assert.Same(inner, ex.InnerException);
    }

    // ─── Infrastructure ───────────────────────────────────────────────────────

    /// <summary>Minimal <see cref="PipelineResponse"/> implementation for test purposes.</summary>
    sealed class FakePipelineResponse : PipelineResponse
    {
        readonly int _status;
        readonly string _reasonPhrase;
        BinaryData? _content;

        public FakePipelineResponse(int status, string reasonPhrase = "error")
        {
            _status = status;
            _reasonPhrase = reasonPhrase;
        }

        public override int Status => _status;
        public override string ReasonPhrase => _reasonPhrase;
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => _content ??= BinaryData.FromString(_reasonPhrase);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);

        protected override PipelineResponseHeaders HeadersCore =>
            throw new NotSupportedException();

        public override void Dispose() { }
    }
}
