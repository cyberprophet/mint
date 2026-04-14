namespace ShareInvest.Agency.Models;

/// <summary>
/// A single shot produced by <see cref="ShareInvest.Agency.OpenAI.GptService.GenerateStudioMintAsync"/>.
/// </summary>
/// <param name="Index">Zero-based index within the batch (0..3 for v1's 4-shot bundle).</param>
/// <param name="ShotType">Stable identifier for the shot style (e.g., "hero-front", "lifestyle",
/// "detail-macro", "alt-angle"). Consumers persist this to distinguish variants.</param>
/// <param name="ImageBytes">Generated PNG bytes, or <see langword="null"/> if this slot failed.</param>
/// <param name="ErrorReason">Non-null when <see cref="ImageBytes"/> is null — short machine-readable
/// reason (e.g., "moderation", "rate_limited", "timeout", "unexpected").</param>
public record StudioMintShot(
    int Index,
    string ShotType,
    BinaryData? ImageBytes,
    string? ErrorReason = null);

/// <summary>
/// Aggregate result of a StudioMint batch generation call.
/// </summary>
/// <param name="Shots">Always 4 entries for v1. Individual entries may have <c>ImageBytes == null</c>
/// when that shot failed; see <see cref="StudioMintShot.ErrorReason"/>.</param>
/// <param name="IsComplete"><see langword="true"/> when every shot succeeded.</param>
public record StudioMintResult(
    IReadOnlyList<StudioMintShot> Shots,
    bool IsComplete);
