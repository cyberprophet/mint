using OpenAI.Images;

namespace ShareInvest.Agency.Models;

/// <summary>
/// Represents the parameters required to request an AI-generated image for a specific scene in a PageMint project.
/// </summary>
/// <param name="UserId">The identifier of the user who owns the project.</param>
/// <param name="Path">The storage path where the generated image will be saved.</param>
/// <param name="SceneId">The identifier of the scene for which the image is being generated.</param>
/// <param name="Prompt">The text prompt describing the image to generate.</param>
/// <param name="AspectRatio">The desired aspect ratio (e.g., "1:1", "16:9", "9:16").</param>
/// <param name="Quality">The desired image quality; defaults to high quality when <see langword="null"/>.</param>
/// <param name="SessionId">Optional session identifier for associating the request with a user session.</param>
public record ImageGenerationRequest(string UserId, string Path, string SceneId, string Prompt, string AspectRatio, GeneratedImageQuality? Quality = null, string? SessionId = null);