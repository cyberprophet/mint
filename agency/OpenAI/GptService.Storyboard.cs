using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Generates a product detail page storyboard using an OpenAI tool-calling loop.
    /// The system prompt is injected by the caller (not embedded) to keep business-sensitive
    /// copywriting prompts out of the public NuGet package.
    /// </summary>
    /// <param name="systemPrompt">Full Apollo system prompt assembled by the caller.</param>
    /// <param name="context">Storyboard generation context (brief, market, visual DNA).</param>
    /// <param name="model">Chat model to use for the copywriting agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each round.</param>
    /// <returns>Parsed <see cref="StoryboardResult"/>, or <see langword="null"/> if the agent did not return valid JSON.</returns>
    public virtual async Task<StoryboardResult?> GenerateStoryboardAsync(
        string systemPrompt,
        StoryboardContext context,
        string model = "gpt-5.4",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        var chatClient = GetChatClient(model);

        var saveStoryboardTool = ChatTool.CreateFunctionTool(
            "save_storyboard",
            "Saves the finalized copy storyboard. Called after copywriting is complete based on the brief.",
            BinaryData.FromString(JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new
                {
                    sections = new
                    {
                        type = "array",
                        description = "Sections of the product detail page (persuasion flow in order)",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                title = new { type = "string", description = "Section title" },
                                strategicIntent = new { type = "string", description = "Marketing purpose of this section in the persuasion flow" },
                                sectionType = new { type = "string", @enum = new[] { "hero", "problem", "routine", "value", "ingredients", "experience", "benefit", "proof", "trust", "summary", "cta", "faq", "spec-table", "how-to-use", "certification" }, description = "Section type" },
                                blocks = new
                                {
                                    type = "array",
                                    description = "Block array. Must include at least one block with type: 'image'.",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            type = new { type = "string", @enum = new[] { "heading", "text", "image", "highlight" } },
                                            content = new { type = "string", description = "Body text, subheading, or image generation prompt. If type is 'image', must be a production-grade visual direction in English, min 100 chars." }
                                        },
                                        required = new[] { "type", "content" }
                                    }
                                }
                            },
                            required = new[] { "title", "strategicIntent", "blocks" }
                        }
                    },
                    ctaText = new { type = "string", description = "Bottom-most purchase call-to-action copy" }
                },
                required = new[] { "sections", "ctaText" }
            })));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 12288,
            Temperature = 0.3f
        };
        options.Tools.Add(saveStoryboardTool);

        var userContent = BuildUserMessage(context);

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userContent)
        };

        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = result.Value;

            if (onUsage is not null && completion.Usage is { } usage)
            {
                onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "storyboard"));
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    if (toolCall.FunctionName != "save_storyboard")
                    {
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, $"Unknown tool: {toolCall.FunctionName}"));
                        continue;
                    }

                    try
                    {
                        var storyboard = JsonSerializer.Deserialize<StoryboardResult>(
                            toolCall.FunctionArguments.ToString(), CaseInsensitiveOptions);

                        if (storyboard is null || storyboard.Sections is null || storyboard.Sections.Length == 0)
                        {
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                                "[Validation Error] sections must contain at least one section."));
                            continue;
                        }

                        // Ensure no section has a null Blocks array (guard against partial deserialization)
                        for (int i = 0; i < storyboard.Sections.Length; i++)
                        {
                            if (storyboard.Sections[i].Blocks is null)
                            {
                                messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                                    $"[Validation Error] Section \"{storyboard.Sections[i].Title}\" has a null blocks array. Every section must include a blocks array."));
                                goto nextToolCall;
                            }
                        }

                        bool autoCorrect = attempt >= maxRetries - 1;

                        if (autoCorrect)
                        {
                            storyboard = AutoCorrectStoryboard(storyboard);
                        }

                        var validationError = ValidateStoryboard(storyboard, context.TargetLanguage, context.ForbiddenCliches, context.ProductType, autoCorrect);

                        if (validationError is not null)
                        {
                            logger.LogWarning("Storyboard validation failed (attempt {Attempt}): {Error}",
                                attempt + 1, validationError.Length > 200 ? validationError[..200] + "..." : validationError);
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, validationError));
                            continue;
                        }

                        return storyboard;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to parse save_storyboard arguments (attempt {Attempt})", attempt + 1);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                            $"[Parse Error] Invalid JSON: {ex.Message}. Rewrite the storyboard as valid JSON."));
                    }

                    nextToolCall:;
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                // Model produced text instead of calling the tool — ask it to use the tool
                var text = completion.Content.FirstOrDefault()?.Text;
                logger.LogWarning("Model returned text instead of calling save_storyboard (attempt {Attempt})", attempt + 1);
                messages.Add(ChatMessage.CreateAssistantMessage(text ?? ""));
                messages.Add(ChatMessage.CreateUserMessage(
                    "You must call the save_storyboard tool with the storyboard JSON. Do not return plain text."));
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        logger.LogWarning("Storyboard generation exhausted {Max} retries", maxRetries);
        return null;
    }

    static string BuildUserMessage(StoryboardContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Brief");
        sb.AppendLine(context.Brief);
        sb.AppendLine();

        sb.AppendLine("## Market Context");
        sb.AppendLine(context.MarketContext);
        sb.AppendLine();

        if (!string.IsNullOrEmpty(context.VisualDna))
        {
            sb.AppendLine("## Visual DNA");
            sb.AppendLine(context.VisualDna);
            sb.AppendLine();
        }

        sb.AppendLine($"## Target Language: {context.TargetLanguage}");
        sb.AppendLine($"MANDATORY: Every heading, text, highlight, and ctaText block MUST be written in {context.TargetLanguage}. " +
            $"Image prompts MUST be in English. On-screen text in image prompts MUST be in {context.TargetLanguage}. " +
            $"Do NOT write copy blocks in any other language — validation will reject them.");

        if (!string.IsNullOrEmpty(context.Feedback))
        {
            sb.AppendLine();
            sb.AppendLine("## Previous Validation Error (FIX THIS)");
            sb.AppendLine(context.Feedback);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Inserts placeholder image blocks into sections that are missing them.
    /// Mirrors the auto-correction logic from P3 save-storyboard.ts.
    /// Returns a new <see cref="StoryboardResult"/> with corrected sections.
    /// </summary>
    static StoryboardResult AutoCorrectStoryboard(StoryboardResult storyboard)
    {
        var correctedSections = storyboard.Sections.Select(section =>
        {
            bool hasImage = section.Blocks.Any(b =>
                string.Equals(b.Type, "image", StringComparison.OrdinalIgnoreCase));

            if (hasImage)
                return section;

            var placeholderPrompt =
                $"Professional product photography style scene depicting: {section.StrategicIntent}, " +
                $"for a {section.SectionType ?? "product detail"} section titled \"{section.Title}\". " +
                "Clean studio lighting from upper left, neutral background, centered composition with generous negative space, warm neutral color palette";

            var correctedBlocks = section.Blocks
                .Append(new StoryboardBlock("image", placeholderPrompt))
                .ToArray();

            return section with { Blocks = correctedBlocks };
        }).ToArray();

        return storyboard with { Sections = correctedSections };
    }

    /// <summary>
    /// Validates a storyboard against quality gates ported from P3 save-storyboard.ts.
    /// Returns null if valid, or an error message string if invalid.
    /// On the final attempt (autoCorrect=true), missing image blocks are already inserted
    /// by <see cref="AutoCorrectStoryboard"/> before this method is called.
    /// </summary>
    string? ValidateStoryboard(StoryboardResult storyboard, string targetLanguage, string[]? forbiddenCliches, string? productType, bool autoCorrect)
    {
        var errors = new List<string>();

        // 1. Image block per section
        var sectionsWithoutImage = storyboard.Sections
            .Where(s => !s.Blocks.Any(b => string.Equals(b.Type, "image", StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.Title)
            .ToArray();

        if (sectionsWithoutImage.Length > 0 && !autoCorrect)
        {
            errors.Add($"[Validation Error] The following sections are missing type: \"image\" blocks: {string.Join(", ", sectionsWithoutImage)}. " +
                "Every section MUST contain at least one block with type: \"image\" and content must be an English AI image generation prompt.");
        }

        // 2. Image prompt language (≥70% Latin)
        foreach (var section in storyboard.Sections)
        {
            foreach (var block in section.Blocks)
            {
                if (!string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                    continue;

                var stripped = StripExemptText(block.Content);
                var ratio = NonLatinRatio(stripped.Length > 0 ? stripped : block.Content);

                if (ratio > 0.3)
                {
                    errors.Add($"[Validation Error] Image prompt in \"{section.Title}\" is not primarily English " +
                        $"(\"{block.Content[..Math.Min(60, block.Content.Length)]}...\"). " +
                        "Rewrite as English production-grade visual directions.");
                }
            }
        }

        // 3. Image prompt minimum length (100 chars)
        foreach (var section in storyboard.Sections)
        {
            foreach (var block in section.Blocks)
            {
                if (!string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (block.Content.Trim().Length < 100)
                {
                    errors.Add($"[Validation Error] Image prompt in \"{section.Title}\" too short ({block.Content.Trim().Length} chars, min 100). " +
                        "Include: visual type, subject, composition, lighting, colors, and mood.");
                }
            }
        }

        // 4. Copy distinctiveness (cliché detection)
        foreach (var section in storyboard.Sections)
        {
            foreach (var block in section.Blocks)
            {
                if (string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = block.Content.Trim();

                foreach (var (pattern, replacement) in GenericCopyPatterns)
                {
                    if (pattern.IsMatch(text))
                    {
                        errors.Add($"[Validation Error] Generic copy in \"{section.Title}\" / {block.Type}: " +
                            $"\"{pattern}\" → {replacement}");
                    }
                }

                // Per-product forbidden clichés from marketContext
                if (forbiddenCliches is not null)
                {
                    foreach (var cliche in forbiddenCliches)
                    {
                        if (!string.IsNullOrWhiteSpace(cliche) && text.Contains(cliche, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"[Validation Error] Forbidden cliché in \"{section.Title}\" / {block.Type}: " +
                                $"\"{cliche}\" → Use product-specific language from the brief's selling points and market context");
                        }
                    }
                }
            }
        }

        // 5. Image prompt self-containment
        foreach (var section in storyboard.Sections)
        {
            foreach (var block in section.Blocks)
            {
                if (!string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var pattern in ExternalRefPatterns)
                {
                    if (pattern.IsMatch(block.Content))
                    {
                        errors.Add($"[Validation Error] Image prompt in \"{section.Title}\" references external context: " +
                            $"\"{pattern}\". Rewrite as a self-contained description.");
                    }
                }
            }
        }

        // 6. On-screen text language consistency
        if (!string.IsNullOrEmpty(targetLanguage))
        {
            foreach (var section in storyboard.Sections)
            {
                foreach (var block in section.Blocks)
                {
                    if (!string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var fragment in ExtractOnScreenText(block.Content))
                    {
                        if (IsExemptFragment(fragment, targetLanguage))
                            continue;

                        if (HasForeignScript(fragment, targetLanguage))
                        {
                            var langName = targetLanguage switch
                            {
                                "ko" => "Korean",
                                "en" => "English",
                                "ja" => "Japanese",
                                "zh" => "Chinese",
                                _ => targetLanguage
                            };
                            errors.Add($"[Validation Error] On-screen text language mismatch in \"{section.Title}\": " +
                                $"\"{fragment}\" → Rewrite in {langName}.");
                        }
                    }
                }
            }
        }

        // 7. Copy block language validation — reject if copy is in wrong language
        if (!string.IsNullOrEmpty(targetLanguage) && targetLanguage != "en")
        {
            foreach (var section in storyboard.Sections)
            {
                foreach (var block in section.Blocks)
                {
                    if (string.Equals(block.Type, "image", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var text = block.Content.Trim();
                    if (text.Length < 10)
                        continue;

                    // Check if copy block is primarily Latin (English) when it should be non-English
                    var latinRatio = 1.0 - NonLatinRatio(text);
                    if (latinRatio > 0.8)
                    {
                        var langName = targetLanguage switch
                        {
                            "ko" => "Korean",
                            "ja" => "Japanese",
                            "zh" => "Chinese",
                            _ => targetLanguage
                        };
                        errors.Add($"[Validation Error] Copy block in \"{section.Title}\" appears to be in English " +
                            $"but target language is {langName}. Rewrite in {langName}: \"{text[..Math.Min(50, text.Length)]}...\"");
                    }
                }
            }
        }

        // 8. Required sections — faq (always) and spec-table (physical products only)
        var sectionTypes = storyboard.Sections
            .Where(s => !string.IsNullOrEmpty(s.SectionType))
            .Select(s => s.SectionType!.ToLowerInvariant())
            .ToHashSet();

        if (!sectionTypes.Contains("faq"))
        {
            errors.Add("[Validation Error] Missing required section: \"faq\". " +
                "Every storyboard MUST include a FAQ section with at least 3 Q&A pairs addressing common purchase anxieties.");
        }

        if (!sectionTypes.Contains("spec-table") && !IsDigitalProduct(productType))
        {
            errors.Add("[Validation Error] Missing required section: \"spec-table\". " +
                "Every storyboard for a physical product MUST include a spec-table section with measurable specifications.");
        }

        return errors.Count > 0 ? string.Join("\n", errors) : null;
    }

    // --- Validation helpers ---

    static bool IsDigitalProduct(string? productType)
    {
        if (string.IsNullOrWhiteSpace(productType))
            return false;

        var lower = productType.ToLowerInvariant();

        // Digital/service keywords — exempt from spec-table requirement
        string[] digitalKeywords =
        [
            "구독", "subscription", "saas", "software", "앱", "app",
            "서비스", "service", "디지털", "digital", "온라인", "online",
            "강의", "course", "전자책", "ebook", "e-book",
            "멤버십", "membership", "라이선스", "license"
        ];

        return digitalKeywords.Any(k => lower.Contains(k));
    }

    static string StripExemptText(string text)
    {
        // Remove quoted and bracketed strings (on-screen text, brand names)
        var result = Regex.Replace(text, "\"[^\"]*\"", "");
        result = Regex.Replace(result, "\u201C[^\u201D]*\u201D", "");
        result = Regex.Replace(result, @"\[[^\]]*\]", "");
        return result.Trim();
    }

    static double NonLatinRatio(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return 0;
        var nonLatin = letters.Count(c => c > '\u024F' && (c < '\u1E00' || c > '\u1EFF'));
        return (double)nonLatin / letters.Length;
    }

    static IEnumerable<string> ExtractOnScreenText(string prompt)
    {
        var fragments = new HashSet<string>();

        foreach (Match m in Regex.Matches(prompt, "'([^']+)'"))
            fragments.Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(prompt, "\"([^\"]+)\""))
            fragments.Add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(prompt, @"(?:text:|labeled|reads|saying)\s*['""]?([^'"",.\n]{2,60})['""]?", RegexOptions.IgnoreCase))
            fragments.Add(m.Groups[1].Value.Trim());

        return fragments;
    }

    static readonly HashSet<string> AllowedAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "CTA", "UI", "UX", "FAQ", "AI", "AR", "VR", "LED", "USB",
        "BEST", "NEW", "HOT", "SALE", "OFF", "FREE", "PRO", "MAX", "PLUS"
    };

    static bool IsExemptFragment(string text, string targetLanguage)
    {
        var t = text.Trim();
        if (Regex.IsMatch(t, @"^[\d%,.]+$")) return true;
        if (AllowedAbbreviations.Contains(t)) return true;

        // In non-English targets, allow ≤2 consecutive Latin-only words (brand names)
        if (targetLanguage != "en")
        {
            var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.All(w => Regex.IsMatch(w, @"^[A-Za-z0-9]+$")) && words.Length <= 2)
                return true;
        }
        return false;
    }

    static bool HasForeignScript(string text, string targetLanguage)
    {
        return targetLanguage switch
        {
            "ko" => HiraganaKatakana.IsMatch(text) || (CjkRange.IsMatch(text) && !HangulRange.IsMatch(text)),
            "en" => HangulRange.IsMatch(text) || HiraganaKatakana.IsMatch(text) || CjkRange.IsMatch(text),
            "ja" => HangulRange.IsMatch(text),
            "zh" => HangulRange.IsMatch(text) || HiraganaKatakana.IsMatch(text),
            _ => false
        };
    }

    static readonly Regex HangulRange = new(@"[\uAC00-\uD7AF\u1100-\u11FF\u3130-\u318F]", RegexOptions.Compiled);
    static readonly Regex HiraganaKatakana = new(@"[\u3040-\u309F\u30A0-\u30FF]", RegexOptions.Compiled);
    static readonly Regex CjkRange = new(@"[\u4E00-\u9FFF\u3400-\u4DBF]", RegexOptions.Compiled);

    static readonly (Regex pattern, string replacement)[] GenericCopyPatterns =
    [
        // Korean clichés
        (new Regex("차이를 경험하세요", RegexOptions.Compiled), "Describe the specific difference the buyer will notice"),
        (new Regex("차이를 느껴보세요", RegexOptions.Compiled), "Name the exact sensation or outcome the buyer gets"),
        (new Regex("차이를 발견하세요", RegexOptions.Compiled), "State what specific discovery the buyer will make"),
        (new Regex(@"프리미엄 경험을?\s?선사", RegexOptions.Compiled), "Describe the concrete experience using product-specific details"),
        (new Regex(@"특별한 경험을?\s?선사", RegexOptions.Compiled), "Replace with a specific benefit tied to the product's selling points"),
        (new Regex("새로운 차원의", RegexOptions.Compiled), "Quantify or describe what specifically improves"),
        (new Regex("완벽한 선택", RegexOptions.Compiled), "Explain why this is the right choice for the target audience's situation"),
        (new Regex("당신만을 위한", RegexOptions.Compiled), "Specify which audience segment and why it fits them"),
        (new Regex("한 차원 높은", RegexOptions.Compiled), "State what is concretely better and by how much"),
        (new Regex("남다른 가치", RegexOptions.Compiled), "Name the specific value and how the buyer benefits"),
        (new Regex("특별함을 선사", RegexOptions.Compiled), "Describe the specific benefit using product features"),
        (new Regex("일상을 바꾸는", RegexOptions.Compiled), "Describe which part of daily life changes and how"),
        (new Regex(@"새로운 기준을?\s?제시", RegexOptions.Compiled), "State what standard is set and with what evidence"),
        (new Regex("차원이 다른", RegexOptions.Compiled), "Specify the concrete difference using measurable outcomes"),
        (new Regex("완벽한 조화", RegexOptions.Compiled), "Describe which elements combine and what the result is"),
        // English clichés
        (new Regex("discover the difference", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Name the specific difference"),
        (new Regex("premium experience", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Describe the experience using product-specific details"),
        (new Regex("take it to the next level", RegexOptions.Compiled | RegexOptions.IgnoreCase), "State what improves and how"),
        (new Regex("elevate your", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Describe the specific improvement"),
        (new Regex("unlock the power", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Name the specific capability"),
        (new Regex("redefine your", RegexOptions.Compiled | RegexOptions.IgnoreCase), "State what changes concretely"),
        (new Regex("like never before", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Compare to a specific prior experience"),
        (new Regex("game.?changer", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Describe what specifically changes"),
        (new Regex("best.in.class", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Cite specific evidence or comparison"),
    ];

    static readonly Regex[] ExternalRefPatterns =
    [
        new("위에서 언급한", RegexOptions.Compiled),
        new("앞서 보여준", RegexOptions.Compiled),
        new("앞서 설명한", RegexOptions.Compiled),
        new("위의 섹션", RegexOptions.Compiled),
        new("mentioned above", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("shown earlier", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("described above", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("as seen in", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("from the previous", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];
}
