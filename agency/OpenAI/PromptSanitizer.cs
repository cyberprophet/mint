namespace ShareInvest.Agency.OpenAI;

/// <summary>
/// Utility for safely inserting user-controlled text into LLM prompts.
/// Wraps input in XML-like delimiters so the model treats it as data,
/// not as instructions (prompt-injection defence — S-12).
/// </summary>
internal static class PromptSanitizer
{
    /// <summary>Maximum number of characters allowed before the input is truncated.</summary>
    internal const int MaxInputLength = 8_000;

    private const string OpenTag  = "<user_input>";
    private const string CloseTag = "</user_input>";

    // Escape sequence used to neutralise any literal closing tag inside user text.
    private const string EscapedCloseTag = "<\u200Buser_input>";   // zero-width space breaks the tag

    /// <summary>
    /// Wraps <paramref name="userInput"/> in <c>&lt;user_input&gt;…&lt;/user_input&gt;</c>
    /// delimiters after escaping any embedded closing tag and truncating overlong input.
    /// Returns an empty-tagged string for <see langword="null"/> or whitespace input.
    /// </summary>
    /// <param name="userInput">Raw text supplied by (or derived from) the end user.</param>
    /// <returns>Sanitized string safe for direct concatenation into an LLM prompt.</returns>
    internal static string EscapeForPrompt(string? userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return $"{OpenTag}{CloseTag}";

        // 1. Truncate to prevent prompt bloat / context exhaustion.
        var text = userInput.Length > MaxInputLength
            ? userInput[..MaxInputLength] + " [truncated]"
            : userInput;

        // 2. Escape any literal </user_input> that would break the delimiter.
        text = text.Replace(CloseTag, EscapedCloseTag, StringComparison.Ordinal);

        return $"{OpenTag}{text}{CloseTag}";
    }
}
