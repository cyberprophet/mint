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
    /// Generates a StudioMint shot bundle from a single product image (Intent 031 / 038).
    /// Each shot runs as a parallel OpenAI image-edit call against the configured image model
    /// (expected to be "gpt-image-1" for v1); the shots use the supplied <paramref name="shots"/>
    /// definitions so downstream consumers can persist a stable <see cref="StudioMintShot.ShotType"/> label.
    /// </summary>
    /// <remarks>
    /// Failures on individual shots are captured on the <see cref="StudioMintShot.ErrorReason"/>
    /// field rather than thrown, so a partial result still surfaces to the caller. The aggregate
    /// <see cref="StudioMintResult.IsComplete"/> flag indicates whether every shot succeeded.
    ///
    /// When <paramref name="shots"/> is <c>null</c>, the method falls back to the internal
    /// v1 hardcoded shot list (<see cref="StudioMintShotTypes.All"/>) so existing callers
    /// that omit the parameter continue to work without modification. This backward-compat
    /// fallback will be removed in 0.17.0 once all consumers pass their own shot definitions.
    ///
    /// Passing an empty list is valid but produces a <see cref="StudioMintResult"/> with zero
    /// shots and <see cref="StudioMintResult.IsComplete"/> = <c>true</c> (vacuously). This is a
    /// caller mistake; document it in the consuming code.
    /// </remarks>
    /// <param name="basePrompt">Full StudioMint base prompt assembled by the caller.</param>
    /// <param name="request">The source image plus optional intent guidance.</param>
    /// <param name="cancellationToken">Cancels the entire batch.</param>
    /// <param name="onUsage">Optional usage callback — invoked once per successful shot.</param>
    /// <param name="shots">
    /// Shot definitions to generate. When <c>null</c>, falls back to the internal v1 defaults
    /// (<see cref="StudioMintShotTypes.All"/>). P5 should always pass an explicit list after
    /// adopting NuGet 0.16.0; the null fallback is a backward-compat bridge only.
    /// Placed last in the parameter list so existing positional callers —
    /// <c>GenerateStudioMintAsync(basePrompt, request, ct)</c> — continue to compile.
    /// </param>
    public virtual async Task<StudioMintResult> GenerateStudioMintAsync(
        string basePrompt,
        StudioMintRequest request,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null,
        IReadOnlyList<StudioMintShotDefinition>? shots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePrompt);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImageFileName);

        var effectiveShots = shots ?? StudioMintShotTypes.All;

        var tasks = effectiveShots.Select((shot, index) =>
            GenerateSingleShotAsync(request, shot, index,
                BuildShotPrompt(basePrompt, shot, request.IntentText), onUsage, cancellationToken)).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return new StudioMintResult(results, IsComplete: results.All(s => s.ImageBytes is not null));
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

            // Tuned for the `gpt-image-2` edit endpoint (default for
            // StudioMint since 0.16.4, configured via `Agency.Models.Image`
            // in P5).
            //
            // `Quality` is intentionally **NOT** set. OpenAI's public
            // REST API accepts `quality=high` on this endpoint (verified
            // 2026-04-24 via curl against gpt-image-2), but the OpenAI
            // .NET SDK 2.10.0 serializes `ImageEditOptions.Quality` in a
            // shape the edit endpoint rejects as
            // `HTTP 400 (invalid_request_error: unknown_parameter: quality)`.
            // The failure was reproduced in an isolated probe against
            // 2.10.0; `Size`/`OutputFileFormat`/`EndUserId` alone work
            // unchanged. Until the SDK fixes the quality serialization,
            // we let the server default (`auto`) pick the quality tier
            // — acceptable for v1 4-cut output. Revisit when the SDK is
            // upgraded past 2.10.0.
            //
            // Kept defaults for 4-cut commerce product shots:
            //   Size             = 1024x1024 (square, PageMint asset ratio)
            //   OutputFileFormat = Png       (lossless, no compression)
            //   EndUserId        = userId    (abuse / support traceability)
            var options = new ImageEditOptions
            {
                Size = GeneratedImageSize.W1024xH1024,
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
                cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (onUsage is not null)
            {
                var usage = result.Value.Usage;
                // StudioMint ships a source image per call, so
                // InputTokenDetails.ImageTokenCount is the key piece
                // that decides the bulk of the cost (billed at the
                // model's image-input rate). Feed it to ApiUsageEvent
                // separately from the text-prompt tokens so
                // ModelPricingTable can price each bucket at its own rate.
                var textInput = (int)(usage?.InputTokenDetails?.TextTokenCount ?? 0);
                var imageInput = (int)(usage?.InputTokenDetails?.ImageTokenCount ?? 0);
                // Fallback: if the SDK response didn't populate the
                // detail struct, treat the combined count as all-image
                // (conservative for this edit flow where the image
                // portion dominates).
                if (textInput == 0 && imageInput == 0)
                    imageInput = (int)(usage?.InputTokenCount ?? 0);
                onUsage(new ApiUsageEvent(
                    ProviderName,
                    imageModel ?? "gpt-image-1",
                    textInput,
                    (int)(usage?.OutputTokenCount ?? 0),
                    $"studio-mint:{shot.Id}",
                    LatencyMs: (int)sw.ElapsedMilliseconds,
                    ImageQuality: "high", ImageSize: "1024x1024",
                    ImageInputTokens: imageInput > 0 ? imageInput : null));
            }

            return new StudioMintShot(index, shot.Id, result.Value[0].ImageBytes);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            var reason = ClassifyBadRequest(ex.Message);
            logger.LogWarning(
                ex,
                "StudioMint shot {ShotType} failed with HTTP 400 ({Reason}): {Message}",
                shot.Id, reason, ex.Message);

            return new StudioMintShot(index, shot.Id, ImageBytes: null, ErrorReason: reason);
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
    /// Classifies an OpenAI HTTP 400 response into a stable <see cref="StudioMintShot.ErrorReason"/>
    /// token. The OpenAI edit endpoint returns 400 for two very different reasons:
    /// content-policy rejections (moderation) and request-shape problems (unknown parameter,
    /// invalid image, etc.). Collapsing both into <c>"moderation"</c> as the old code did
    /// masked real bugs (e.g., the <c>output_format</c> regression in 0.16.2) behind a
    /// user-safe-looking label.
    /// </summary>
    /// <remarks>
    /// We look at the plain-text body of the exception since <c>ClientResultException</c>
    /// doesn't expose OpenAI's <c>error.code</c> field as a structured property. OpenAI's
    /// moderation rejections carry identifiable markers — <c>moderation_blocked</c> or
    /// <c>content_policy_violation</c>. Anything else in 400 territory is treated as
    /// <c>bad_request</c> so the failure surfaces honestly in logs and the DB.
    /// </remarks>
    internal static string ClassifyBadRequest(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "bad_request";
        if (message.Contains("moderation_blocked", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("content_policy_violation", StringComparison.OrdinalIgnoreCase))
            return "moderation";
        return "bad_request";
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
/// Defines one shot in a StudioMint generation pack. Promoted to <c>public</c> in 0.16.0
/// so P5 can construct its own shot definitions from external MD files and pass them to
/// <see cref="GptService.GenerateStudioMintAsync"/> without relying on the internal defaults.
/// For the rev.3 industry 4-cut pack the fields come from P5 MD files
/// (<c>shot-cutout.md</c> / <c>shot-styled.md</c> / <c>shot-detail.md</c> / <c>shot-special.md</c>).
/// </summary>
public sealed record StudioMintShotDefinition
{
    /// <summary>
    /// Stable identifier persisted with the generated shot (e.g., <c>"cutout"</c>, <c>"styled"</c>).
    /// </summary>
    public string Id { get; }

    /// <summary>Human-readable shot name included in the prompt direction header.</summary>
    public string Label { get; }

    /// <summary>Full shooting-language description injected by <c>BuildShotPrompt</c>.</summary>
    public string Direction { get; }

    /// <summary>
    /// Constructs a shot definition and validates that all three fields are non-null,
    /// non-empty, and non-whitespace. Introduced in 0.16.0 alongside the <c>public</c>
    /// promotion so external callers (P5 and third-party NuGet consumers) cannot
    /// accidentally ship malformed shot definitions that would silently corrupt the
    /// generated prompt.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="Id"/>, <paramref name="Label"/>, or
    /// <paramref name="Direction"/> is null, empty, or whitespace.
    /// </exception>
    public StudioMintShotDefinition(string Id, string Label, string Direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(Direction);

        this.Id = Id;
        this.Label = Label;
        this.Direction = Direction;
    }

    /// <summary>Deconstructs the record into its three components.</summary>
    public void Deconstruct(out string Id, out string Label, out string Direction)
    {
        Id = this.Id;
        Label = this.Label;
        Direction = this.Direction;
    }
}

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
