using System.Text.RegularExpressions;

namespace ShareInvest.Agency.OpenAI;

/// <summary>
/// Utility for safely inserting user-controlled text into LLM prompts.
/// Wraps input in XML-like delimiters so the model treats it as data,
/// not as instructions (prompt-injection defence — S-12).
/// </summary>
internal static partial class PromptSanitizer
{
    /// <summary>
    /// Maximum number of characters allowed before the input is truncated.
    /// Set high enough (100 KB) that legitimate serialized JSON payloads are never affected.
    /// </summary>
    internal const int MaxInputLength = 100_000;

    private const string OpenTag  = "<user_input>";
    private const string CloseTag = "</user_input>";

    // Escape sequences used to neutralise embedded delimiter tags inside user text.
    // HTML entity encoding is used so that neither escape string contains an angle-bracket
    // sequence that could be matched (literally or under Unicode collation) as a real tag.
    private const string EscapedOpenTag  = "&lt;user_input&gt;";
    private const string EscapedCloseTag = "&lt;/user_input&gt;";

    // Matches <user_input> with optional whitespace before '>' — e.g. "<user_input >" or "<user_input\t>"
    [GeneratedRegex(@"<user_input\s*>", RegexOptions.Compiled)]
    private static partial Regex OpenTagVariantsRegex();

    // Matches </user_input> with optional whitespace before '>' — e.g. "</user_input >" or "</user_input\t>"
    [GeneratedRegex(@"</user_input\s*>", RegexOptions.Compiled)]
    private static partial Regex CloseTagVariantsRegex();

    /// <summary>
    /// Wraps <paramref name="userInput"/> in <c>&lt;user_input&gt;…&lt;/user_input&gt;</c>
    /// delimiters after escaping any embedded delimiter tags and truncating overlong input.
    /// Returns an empty-tagged string for <see langword="null"/> or whitespace input.
    /// </summary>
    /// <param name="userInput">Raw text supplied by (or derived from) the end user.</param>
    /// <returns>Sanitized string safe for direct concatenation into an LLM prompt.</returns>
    internal static string EscapeForPrompt(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return $"{OpenTag}{CloseTag}";

        // 1. Truncate to prevent prompt bloat / context exhaustion.
        //    MaxInputLength is set to 100 KB so normal JSON payloads are never affected.
        var text = userInput.Length > MaxInputLength
            ? userInput[..MaxInputLength] + " [truncated]"
            : userInput;

        // 2. Escape any closing-tag variants (e.g. </user_input>, </user_input >, </user_input\t>)
        //    that could break out of the delimiter.
        text = CloseTagVariantsRegex().Replace(text, EscapedCloseTag);

        // 3. Escape any opening-tag variants (e.g. <user_input>, <user_input >) to prevent
        //    the model from seeing a nested/duplicate opening delimiter in user content.
        text = OpenTagVariantsRegex().Replace(text, EscapedOpenTag);

        return $"{OpenTag}{text}{CloseTag}";
    }

    /// <summary>
    /// HTML-escapes structural identifiers (e.g., document ids) for safe inclusion in prompt
    /// markup WITHOUT wrapping in <c>&lt;user_input&gt;</c> tags. Used when the model is
    /// instructed to echo the id verbatim: wrapping would cause the model to round-trip the
    /// wrapper tags, breaking downstream id-equality checks.
    /// </summary>
    /// <remarks>
    /// Only replaces the characters that could break the surrounding attribute/element
    /// context (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>"</c>). All other characters —
    /// including the document id's original casing and punctuation — pass through unchanged
    /// so <c>knownIds.Contains(id)</c> still succeeds on the parsed output.
    /// </remarks>
    internal static string EscapeIdentifierForPrompt(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return string.Empty;

        return identifier
            .Replace("&",  "&amp;")
            .Replace("<",  "&lt;")
            .Replace(">",  "&gt;")
            .Replace("\"", "&quot;");
    }
}
