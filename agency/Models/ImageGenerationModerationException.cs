namespace ShareInvest.Agency.Models;

/// <summary>
/// Thrown when OpenAI's safety system blocks an image generation request.
/// </summary>
public class ImageGenerationModerationException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="ImageGenerationModerationException"/> with the moderation rejection message.
    /// </summary>
    /// <param name="message">The message describing why the image generation request was rejected.</param>
    public ImageGenerationModerationException(string message) : base(message)
    {

    }

    /// <summary>
    /// Initializes a new instance of <see cref="ImageGenerationModerationException"/> with a rejection message and an inner exception.
    /// </summary>
    /// <param name="message">The message describing why the image generation request was rejected.</param>
    /// <param name="innerException">The underlying exception that caused this exception to be thrown.</param>
    public ImageGenerationModerationException(string message, Exception innerException) : base(message, innerException)
    {

    }
}