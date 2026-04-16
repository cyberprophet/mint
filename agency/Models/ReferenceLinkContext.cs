namespace ShareInvest.Agency.Models;

/// <summary>
/// Contextual hints provided to the reference-link analyst to guide extraction.
/// </summary>
/// <param name="TargetLanguage">
/// BCP-47 language tag of the landing page being produced (e.g., "ko", "en").
/// Used to orient tone/messaging analysis toward the correct audience.
/// </param>
/// <param name="ProductName">
/// Optional product name hint. When provided, the analyst can better distinguish
/// the reference page's own product from the inspiration being extracted.
/// </param>
public record ReferenceLinkContext(
    string TargetLanguage,
    string? ProductName = null);
