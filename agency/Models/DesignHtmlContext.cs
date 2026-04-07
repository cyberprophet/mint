namespace ShareInvest.Agency.Models;

/// <summary>
/// Context for Athena HTML design generation: blueprint + storyboard + optional brief/feedback.
/// </summary>
public record DesignHtmlContext(
    BlueprintResult Blueprint,
    StoryboardResult Storyboard,
    object? Brief,
    string? Feedback);
