namespace ShareInvest.Agency.Models;

/// <summary>
/// Thrown when the OpenAI Images API returns HTTP 429 (rate limit exceeded).
/// Callers can catch this to implement back-off or surface a user-friendly message.
/// </summary>
public class ImageRateLimitedException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="ImageRateLimitedException"/> with a rate-limit message.
    /// </summary>
    /// <param name="message">Details about the rate-limit condition.</param>
    public ImageRateLimitedException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ImageRateLimitedException"/> with a rate-limit message and inner exception.
    /// </summary>
    /// <param name="message">Details about the rate-limit condition.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ImageRateLimitedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
