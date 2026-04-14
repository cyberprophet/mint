namespace ShareInvest.Agency.Models;

/// <summary>
/// Parameters for a StudioMint 4-shot generation request (Intent 031).
/// The agent takes a user-supplied product photo and produces a bundled
/// set of studio-quality shots that can be dropped into the PageMint
/// planning asset library.
/// </summary>
/// <param name="UserId">The identifier of the user who owns the request.</param>
/// <param name="SourceImage">Raw PNG/JPEG bytes of the product photo to enrich.</param>
/// <param name="SourceImageFileName">Filename including extension (e.g., "product.png"). The OpenAI
/// Images API validates the format from this extension.</param>
/// <param name="IntentText">Optional free-text guidance — brand mood, target audience, must-preserve
/// details. Appended to every shot prompt verbatim.</param>
/// <param name="SessionId">Optional session identifier for correlating the request with a chat session.</param>
public record StudioMintRequest(
    string UserId,
    BinaryData SourceImage,
    string SourceImageFileName,
    string? IntentText = null,
    string? SessionId = null);
