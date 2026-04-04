#pragma warning disable OPENAI001

using OpenAI.Images;

namespace ShareInvest.Agency.Models;

/// <summary>
/// Re-exports <see cref="GeneratedImageQuality"/> so consumers don't need a direct OpenAI dependency.
/// </summary>
public static class ImageQuality
{
    /// <summary>Standard quality — faster, lower cost.</summary>
    public static GeneratedImageQuality Medium => GeneratedImageQuality.MediumQuality;

    /// <summary>High quality — slower, higher detail.</summary>
    public static GeneratedImageQuality High => GeneratedImageQuality.HighQuality;
}
