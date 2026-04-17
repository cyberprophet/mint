using Microsoft.Extensions.Logging;

using OpenAI.Images;

using ShareInvest.Agency.Models;

using System.ClientModel;
using System.Diagnostics;

#pragma warning disable OPENAI001

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Generates a StudioMint 4-shot bundle from a single product image (Intent 031).
    /// Each shot runs as a parallel OpenAI image-edit call against the configured image model
    /// (expected to be "gpt-image-1" for v1); the four shots use fixed style variants so
    /// downstream consumers can persist a stable <see cref="StudioMintShot.ShotType"/> label.
    /// </summary>
    /// <remarks>
    /// Failures on individual shots are captured on the <see cref="StudioMintShot.ErrorReason"/>
    /// field rather than thrown, so a partial result still surfaces to the caller. The aggregate
    /// <see cref="StudioMintResult.IsComplete"/> flag indicates whether every shot succeeded.
    /// </remarks>
    /// <param name="basePrompt">Full StudioMint base prompt assembled by the caller.</param>
    /// <param name="request">The source image plus optional intent guidance.</param>
    /// <param name="cancellationToken">Cancels the entire batch.</param>
    /// <param name="onUsage">Optional usage callback — invoked once per successful shot.</param>
    public virtual async Task<StudioMintResult> GenerateStudioMintAsync(
        string basePrompt,
        StudioMintRequest request,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePrompt);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImageFileName);

        var tasks = StudioMintShotTypes.All.Select((shot, index) =>
            GenerateSingleShotAsync(request, shot, index,
                BuildShotPrompt(basePrompt, shot, request.IntentText), onUsage, cancellationToken)).ToArray();

        var shots = await Task.WhenAll(tasks);

        return new StudioMintResult(shots, IsComplete: shots.All(s => s.ImageBytes is not null));
    }

    async Task<StudioMintShot> GenerateSingleShotAsync(
        StudioMintRequest request,
        StudioMintShotDefinition shot,
        int index,
        string prompt,
        Action<ApiUsageEvent>? onUsage,
        CancellationToken cancellationToken)
    {
        try
        {
            var imageClient = GetImageClient(imageModel);

            var options = new ImageEditOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
                Quality = GeneratedImageQuality.High,
                OutputFileFormat = GeneratedImageFileFormat.Png,
                EndUserId = request.UserId
            };

            using var sourceStream = request.SourceImage.ToStream();

            var sw = Stopwatch.StartNew();
            ClientResult<GeneratedImageCollection> result = await imageClient.GenerateImageEditsAsync(
                sourceStream,
                request.SourceImageFileName,
                prompt,
                imageCount: 1,
                options,
                cancellationToken);
            sw.Stop();

            if (onUsage is not null)
            {
                var usage = result.Value.Usage;
                onUsage(new ApiUsageEvent(
                    "openai",
                    imageModel ?? "gpt-image-1",
                    (int)(usage?.InputTokenCount ?? 0),
                    (int)(usage?.OutputTokenCount ?? 0),
                    $"studio-mint:{shot.Id}",
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    ImageQuality: "high", ImageSize: "1024x1024"));
            }

            return new StudioMintShot(index, shot.Id, result.Value[0].ImageBytes);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            logger.LogWarning(ex, "StudioMint shot {ShotType} blocked by moderation: {Message}", shot.Id, ex.Message);

            return new StudioMintShot(index, shot.Id, ImageBytes: null, ErrorReason: "moderation");
        }
        catch (ClientResultException ex) when (ex.Status == 401)
        {
            logger.LogError(ex, "StudioMint shot {ShotType} authentication failed — check API key", shot.Id);

            throw new UnauthorizedAccessException(
                "OpenAI image-edit authentication failed. Verify the API key is valid and has image generation permissions.",
                ex);
        }
        catch (ClientResultException ex) when (ex.Status == 429)
        {
            logger.LogWarning(ex, "StudioMint shot {ShotType} rate limited", shot.Id);

            return new StudioMintShot(index, shot.Id, ImageBytes: null, ErrorReason: "rate_limited");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StudioMint shot {ShotType} failed unexpectedly", shot.Id);

            return new StudioMintShot(index, shot.Id, ImageBytes: null, ErrorReason: "unexpected");
        }
    }

    /// <summary>
    /// Composes the full prompt sent to the OpenAI image-edit endpoint for one shot.
    /// Extracted to a pure helper so deterministic prompt-shape tests don't need to
    /// mock the entire OpenAI client.
    /// </summary>
    internal static string BuildShotPrompt(string basePrompt, StudioMintShotDefinition shot, string? intentText)
    {
        var intentSuffix = string.IsNullOrWhiteSpace(intentText)
            ? string.Empty
            : $"\n\nAdditional guidance from the user:\n{intentText.Trim()}";

        return $"{basePrompt}\n\nShot direction — {shot.Label}:\n{shot.Direction}{intentSuffix}";
    }
}

/// <summary>
/// One of the four StudioMint v1 shot slots. Kept as a private record so the specific
/// prompts remain internal implementation detail — consumers only see the <see cref="Id"/>.
/// </summary>
internal sealed record StudioMintShotDefinition(string Id, string Label, string Direction);

internal static class StudioMintShotTypes
{
    public static readonly IReadOnlyList<StudioMintShotDefinition> All =
    [
        new(
            Id: "hero-front",
            Label: "Hero front",
            Direction: "Centered hero composition. Product faces the camera head-on, filling roughly 60% of the frame. Seamless neutral studio background (soft light gray or off-white). Balanced key + fill lighting with subtle reflections on the product's surface. This is the primary marketing shot."),
        new(
            Id: "lifestyle",
            Label: "Lifestyle context",
            Direction: "Place the product in a natural in-use context that implies how a target customer would interact with it — a clean desk, kitchen counter, bag, or relevant environment. Ambient daylight-style lighting. Supporting props may appear softly out of focus, but the product remains the clear subject and stays in sharp focus."),
        new(
            Id: "detail-macro",
            Label: "Detail macro",
            Direction: "Tight macro shot emphasising surface texture, finish, and build quality. Fill the frame with the product's most distinctive feature (e.g., seam, material, logo as-is on the product, control, grip). Shallow depth of field, precise focus on the texture, directional side lighting to reveal contour."),
        new(
            Id: "alt-angle",
            Label: "Alternate angle",
            Direction: "Three-quarter or top-down alternate angle that complements the hero shot. Reveal a side or detail the hero view cannot show. Same neutral studio background and lighting treatment as the hero shot so the two read as a paired set.")
    ];
}
