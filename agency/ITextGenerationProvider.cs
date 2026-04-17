using ShareInvest.Agency.Models;

namespace ShareInvest.Agency;

/// <summary>
/// Abstraction for text generation capabilities across AI providers.
/// Implementations: <see cref="OpenAI.GptService"/> (OpenAI / OpenAI-compatible),
/// <see cref="Google.GeminiProvider"/> (Google Gemini).
/// </summary>
public interface ITextGenerationProvider : IDisposable
{
    /// <summary>Provider identifier for telemetry (e.g., "openai", "gemini", "groq").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates a short title (≤50 chars) summarising conversation text.
    /// </summary>
    Task<string?> GenerateTitleAsync(
        string systemPrompt,
        string conversationText,
        string model,
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a structured storyboard (Apollo copywriting engine).
    /// </summary>
    Task<StoryboardResult?> GenerateStoryboardAsync(
        string systemPrompt,
        StoryboardContext context,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);

    /// <summary>
    /// Generates a layout blueprint (Pygmalion blueprint engine).
    /// </summary>
    Task<BlueprintResult?> GenerateBlueprintAsync(
        string systemPrompt,
        BlueprintContext context,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);

    /// <summary>
    /// Generates Tailwind CSS HTML design (Athena design engine).
    /// </summary>
    Task<DesignHtmlResult?> GenerateDesignHtmlAsync(
        string systemPrompt,
        DesignHtmlContext context,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);

    /// <summary>
    /// Conducts multi-step product research with web search tools.
    /// </summary>
    Task<ResearchResult?> ResearchProductAsync(
        string systemPrompt,
        string productInfo,
        string[] urls,
        string? category,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);

    /// <summary>
    /// Extracts structured product information from documents.
    /// </summary>
    Task<ProductInfoResult?> ExtractProductInfoAsync(
        string systemPrompt,
        IReadOnlyList<ProductInfoDocument> documents,
        string model,
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null);

    /// <summary>
    /// Analyzes a reference web page for design/copy inspiration DNA.
    /// </summary>
    Task<ReferenceLinkAnalysis?> AnalyzeReferenceLinkAsync(
        string systemPrompt,
        string url,
        string html,
        ReferenceLinkContext context,
        Action<ApiUsageEvent>? onUsage = null,
        CancellationToken cancellationToken = default);
}
