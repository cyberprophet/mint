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
    /// Generates a layout blueprint using an OpenAI tool-calling loop.
    /// The system prompt is injected by the caller (not embedded) to keep business-sensitive
    /// blueprint strategy prompts out of the public NuGet package.
    /// </summary>
    /// <param name="systemPrompt">Full Pygmalion system prompt assembled by the caller.</param>
    /// <param name="context">Blueprint generation context (storyboard, visual DNA, brief).</param>
    /// <param name="model">Chat model to use for the blueprint architect agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after each round.</param>
    /// <returns>Parsed <see cref="BlueprintResult"/>, or <see langword="null"/> if the agent did not return valid JSON.</returns>
    public virtual async Task<BlueprintResult?> GenerateBlueprintAsync(
        string systemPrompt,
        BlueprintContext context,
        string model = "gpt-5.4",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        var chatClient = GetChatClient(model);

        var generateBlueprintTool = ChatTool.CreateFunctionTool(
            "generate_layout_blueprint",
            "Saves the layout blueprint with page design system, visual blocks, and photo-only asset slot prompts.",
            BinaryData.FromString(JsonSerializer.Serialize(BlueprintToolSchema)));

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 16384,
            Temperature = 0.3f
        };
        options.Tools.Add(generateBlueprintTool);

        var userContent = BuildBlueprintUserMessage(context);

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
                onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount, "blueprint"));
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                messages.Add(ChatMessage.CreateAssistantMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    if (toolCall.FunctionName != "generate_layout_blueprint")
                    {
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, $"Unknown tool: {toolCall.FunctionName}"));
                        continue;
                    }

                    try
                    {
                        var blueprint = JsonSerializer.Deserialize<BlueprintResult>(
                            toolCall.FunctionArguments.ToString(), CaseInsensitiveOptions);

                        if (blueprint is null || blueprint.VisualBlocks is null || blueprint.VisualBlocks.Length == 0)
                        {
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                                "[Validation Error] visualBlocks must contain at least one block."));
                            continue;
                        }

                        if (blueprint.PageDesignSystem is null)
                        {
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                                "[Validation Error] pageDesignSystem is required."));
                            continue;
                        }

                        var validationError = ValidateBlueprint(blueprint);

                        if (validationError is not null)
                        {
                            logger.LogWarning("Blueprint validation failed (attempt {Attempt}): {Error}",
                                attempt + 1, validationError.Length > 300 ? validationError[..300] + "..." : validationError);
                            messages.Add(ChatMessage.CreateToolMessage(toolCall.Id, validationError));
                            continue;
                        }

                        return blueprint;
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to parse generate_layout_blueprint arguments (attempt {Attempt})", attempt + 1);
                        messages.Add(ChatMessage.CreateToolMessage(toolCall.Id,
                            $"[Parse Error] Invalid JSON: {ex.Message}. Rewrite the blueprint as valid JSON."));
                    }
                }
            }
            else if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var text = completion.Content.FirstOrDefault()?.Text;
                logger.LogWarning("Model returned text instead of calling generate_layout_blueprint (attempt {Attempt})", attempt + 1);
                messages.Add(ChatMessage.CreateAssistantMessage(text ?? ""));
                messages.Add(ChatMessage.CreateUserMessage(
                    "You must call the generate_layout_blueprint tool with the blueprint JSON. Do not return plain text."));
            }
            else
            {
                logger.LogWarning("Unexpected finish reason: {Reason}", completion.FinishReason);
                break;
            }
        }

        logger.LogWarning("Blueprint generation exhausted {Max} retries", maxRetries);
        return null;
    }

    static string BuildBlueprintUserMessage(BlueprintContext context)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Storyboard");
        sb.AppendLine(PromptSanitizer.EscapeForPrompt(context.StoryboardJson));
        sb.AppendLine();

        if (!string.IsNullOrEmpty(context.VisualDna))
        {
            sb.AppendLine("## Visual DNA");
            sb.AppendLine(PromptSanitizer.EscapeForPrompt(context.VisualDna));
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("## Visual DNA");
            sb.AppendLine("None (no product images analyzed — derive pageDesignSystem from brief fields)");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(context.BriefJson))
        {
            sb.AppendLine("## Brief Context");
            sb.AppendLine(PromptSanitizer.EscapeForPrompt(context.BriefJson));
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(context.Feedback))
        {
            sb.AppendLine("## Previous Validation Error (FIX THIS)");
            sb.AppendLine(PromptSanitizer.EscapeForPrompt(context.Feedback));
        }

        return sb.ToString();
    }

    // ─── Blueprint Validation (15 gates) ──────────────────────────

    internal string? ValidateBlueprint(BlueprintResult blueprint)
    {
        var errors = new List<string>();
        var pds = blueprint.PageDesignSystem;

        // PageDesignSystem completeness
        if (string.IsNullOrWhiteSpace(pds.Mood))
            errors.Add("[Validation Error] pageDesignSystem.mood is required.");
        if (pds.BrandColors is null || pds.BrandColors.Length == 0)
            errors.Add("[Validation Error] pageDesignSystem.brandColors must have at least 1 color.");
        if (string.IsNullOrWhiteSpace(pds.BackgroundApproach))
            errors.Add("[Validation Error] pageDesignSystem.backgroundApproach is required.");
        if (string.IsNullOrWhiteSpace(pds.TypographyScale))
            errors.Add("[Validation Error] pageDesignSystem.typographyScale is required.");

        foreach (var block in blueprint.VisualBlocks)
        {
            // Gate 4: Valid blockType
            if (!ValidBlockTypes.Contains(block.BlockType))
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": invalid blockType \"{block.BlockType}\". " +
                    $"Must be one of: {string.Join(", ", ValidBlockTypes)}");
                continue; // Skip further block-level checks if blockType is invalid
            }

            // Gate 5: layoutVariant per blockType vocabulary
            if (LayoutVariantVocabulary.TryGetValue(block.BlockType, out var allowedVariants)
                && !allowedVariants.Contains(block.LayoutVariant))
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": layoutVariant \"{block.LayoutVariant}\" " +
                    $"is not valid for blockType \"{block.BlockType}\". Allowed: {string.Join(", ", allowedVariants)}");
            }

            // Gate 6: Minimum panel count per blockType
            if (MinPanels.TryGetValue(block.BlockType, out var minPanels)
                && (block.Panels?.Length ?? 0) < minPanels)
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": blockType \"{block.BlockType}\" requires " +
                    $"at least {minPanels} panel(s), got {block.Panels?.Length ?? 0}.");
            }

            // Gate 8: Hero heightWeight = "xl"
            if (block.BlockType == "hero" && block.HeightWeight != "xl")
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": hero blocks must use heightWeight \"xl\", " +
                    $"got \"{block.HeightWeight}\".");
            }

            // Gate 9: CTA heightWeight = "short" or "medium"
            if (block.BlockType == "offer-reassurance-sticky"
                && block.HeightWeight is "xl" or "large")
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": CTA blocks should use \"short\" or \"medium\", " +
                    $"got \"{block.HeightWeight}\".");
            }

            // Gate 7: Multi-scene slot-per-panel (triptych, timeline)
            if (block.BlockType is "vertical-triptych" or "timeline")
            {
                var panelCount = block.Panels?.Length ?? 0;
                var slotCount = block.AssetSlots?.Length ?? 0;

                if (slotCount < panelCount)
                {
                    errors.Add($"[Validation Error] Block \"{block.BlockId}\": blockType \"{block.BlockType}\" should have " +
                        $"at least one assetSlot per panel ({panelCount} panels, {slotCount} slots). " +
                        "Each scene/step needs its own photo — do not combine multiple scenes into one image.");
                }
            }

            // Validate each asset slot
            if (block.AssetSlots is null || block.AssetSlots.Length == 0)
            {
                errors.Add($"[Validation Error] Block \"{block.BlockId}\": must have at least 1 assetSlot.");
                continue;
            }

            foreach (var slot in block.AssetSlots)
            {
                // Gate 1: Forbidden prompt patterns
                foreach (var pattern in ForbiddenPromptPatterns)
                {
                    if (pattern.IsMatch(slot.Prompt))
                    {
                        errors.Add($"[Validation Error] Asset slot \"{slot.SlotId}\" in block \"{block.BlockId}\": " +
                            $"prompt matches forbidden pattern \"{pattern}\". " +
                            "Asset slot prompts must describe photo/illustration only — no Korean text, no UI components, " +
                            "no layout structure, no composite requests.");
                        break; // One forbidden pattern per slot is enough
                    }
                }

                // Gate 2: Prompt minimum length (80 chars)
                if (slot.Prompt.Trim().Length < 80)
                {
                    errors.Add($"[Validation Error] Asset slot \"{slot.SlotId}\" in block \"{block.BlockId}\": " +
                        $"prompt too short ({slot.Prompt.Trim().Length} chars, min 80). " +
                        "Include: SUBJECT, ENVIRONMENT, LIGHTING, MOOD, COMPOSITION, PALETTE, NEGATIVE SPACE.");
                }

                // Gate 3: Required negativeConstraints
                if (slot.NegativeConstraints is null || slot.NegativeConstraints.Length == 0)
                {
                    errors.Add($"[Validation Error] Asset slot \"{slot.SlotId}\" in block \"{block.BlockId}\": " +
                        "negativeConstraints must be specified.");
                }
                else
                {
                    var lower = slot.NegativeConstraints.Select(c => c.ToLowerInvariant()).ToArray();

                    foreach (var required in RequiredNegativeConstraints)
                    {
                        if (!lower.Any(c => c.Contains(required)))
                        {
                            errors.Add($"[Validation Error] Asset slot \"{slot.SlotId}\" in block \"{block.BlockId}\": " +
                                $"negativeConstraints must include \"{required}\".");
                        }
                    }
                }
            }
        }

        // Gate 10: No 3+ consecutive same blockType
        var blocks = blueprint.VisualBlocks;

        for (int i = 2; i < blocks.Length; i++)
        {
            if (blocks[i].BlockType == blocks[i - 1].BlockType && blocks[i].BlockType == blocks[i - 2].BlockType)
            {
                errors.Add($"[Rhythm Error] blockType \"{blocks[i].BlockType}\" appears 3 times consecutively " +
                    $"(blocks {i - 2}–{i}). Vary block types to create visual rhythm.");
            }
        }

        // Gate 11: No 3+ consecutive same heightWeight
        for (int i = 2; i < blocks.Length; i++)
        {
            if (blocks[i].HeightWeight == blocks[i - 1].HeightWeight && blocks[i].HeightWeight == blocks[i - 2].HeightWeight)
            {
                errors.Add($"[Rhythm Error] heightWeight \"{blocks[i].HeightWeight}\" appears 3 times consecutively " +
                    $"(blocks {i - 2}–{i}). Vary height weights to create scroll rhythm.");
            }
        }

        // Gate 12: Override abuse warning (non-blocking, appended as hint)
        // BackgroundApproach is excluded: Gate 13 specifically encourages background cycling,
        // so counting it here would contradict that guidance.
        var overrideCount = blocks.Count(b => b.DesignOverrides is not null
            && (!string.IsNullOrEmpty(b.DesignOverrides.Mood)
                || !string.IsNullOrEmpty(b.DesignOverrides.TypographyScale)));
        var overrideRatio = blocks.Length > 0 ? (double)overrideCount / blocks.Length : 0;

        if (overrideRatio > 0.4 && errors.Count == 0)
        {
            // Non-blocking: return null (valid) but log warning
            logger.LogWarning("Blueprint has {Percent}% blocks with designOverrides — consider using pageDesignSystem more",
                Math.Round(overrideRatio * 100));
        }

        // Gate 13: Background cycling — soft warning when too few blocks specify a backgroundApproach.
        var blocksWithBackground = blocks.Count(b => !string.IsNullOrWhiteSpace(b.DesignOverrides?.BackgroundApproach));
        if (blocks.Length >= 6 && blocksWithBackground * 2 < blocks.Length)
        {
            logger.LogWarning(
                "Blueprint has only {Count}/{Total} blocks with backgroundApproach specified — professional pages need background cycling for visual rhythm",
                blocksWithBackground, blocks.Length);
        }

        // Gate 14: Block type diversity
        var blockTypeCounts = blocks
            .GroupBy(b => b.BlockType)
            .ToDictionary(g => g.Key, g => g.Count());
        var uniqueTypes = blockTypeCounts.Count;

        if (blocks.Length >= 8 && uniqueTypes < 5)
            errors.Add($"Page has {blocks.Length} blocks but only {uniqueTypes} unique blockTypes (need ≥5). Vary block types for visual diversity.");
        else if (blocks.Length >= 5 && uniqueTypes < 4)
            errors.Add($"Page has {blocks.Length} blocks but only {uniqueTypes} unique blockTypes (need ≥4). Vary block types for visual diversity.");

        // No single non-hero/CTA type > 30% (only for pages with 5+ blocks where the cap is satisfiable)
        if (blocks.Length >= 5)
        {
            foreach (var kvp in blockTypeCounts)
            {
                if (kvp.Key is "hero" or "offer-reassurance-sticky") continue;
                if ((double)kvp.Value / blocks.Length > 0.3)
                    errors.Add($"blockType \"{kvp.Key}\" appears {kvp.Value}/{blocks.Length} times (>30%). Use different block types for variety.");
            }
        }

        // Gate 15: No repeated layoutVariant for same blockType
        var variantsByType = blocks
            .GroupBy(b => b.BlockType)
            .Where(g => g.Count() > 1);

        foreach (var group in variantsByType)
        {
            var variants = group.Select(b => b.LayoutVariant).ToList();
            var duplicates = variants.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0)
                errors.Add($"blockType \"{group.Key}\" reuses layoutVariant \"{string.Join(", ", duplicates)}\". Use different variants when repeating a blockType.");
        }

        return errors.Count > 0 ? string.Join("\n", errors) : null;
    }

    // ─── Blueprint Constants ──────────────────────────────────

    static readonly string[] RequiredNegativeConstraints = ["no text", "no ui elements", "no buttons", "no captions"];

    static readonly HashSet<string> ValidBlockTypes =
    [
        "hero", "vertical-triptych", "value-benefit", "comparison-split",
        "proof-trust", "benefit-grid", "timeline", "offer-reassurance-sticky"
    ];

    static readonly Dictionary<string, string[]> LayoutVariantVocabulary = new()
    {
        ["hero"] = ["full-bleed-center", "offset-product-stack", "split-hero"],
        ["vertical-triptych"] = ["three-row-equal", "top-dominant-two-below", "staggered-alternating"],
        ["value-benefit"] = ["dominant-visual-with-context", "split-feature", "single-showcase"],
        ["comparison-split"] = ["side-by-side", "top-bottom-contrast"],
        ["proof-trust"] = ["evidence-showcase", "exploded-detail", "multi-evidence-strip"],
        ["benefit-grid"] = ["asymmetric-bento", "equal-grid", "stacked-cards"],
        ["timeline"] = ["vertical-step-ribbon", "horizontal-flow"],
        ["offer-reassurance-sticky"] = ["product-anchor-cta", "summary-then-cta"]
    };

    static readonly Dictionary<string, int> MinPanels = new()
    {
        ["hero"] = 1,
        ["vertical-triptych"] = 2,
        ["value-benefit"] = 1,
        ["comparison-split"] = 2,
        ["proof-trust"] = 1,
        ["benefit-grid"] = 2,
        ["timeline"] = 2,
        ["offer-reassurance-sticky"] = 1
    };

    /// <summary>
    /// 41 forbidden prompt patterns ported from P3 generate-layout-blueprint.ts.
    /// Asset slot prompts must describe photo/illustration only.
    /// </summary>
    static readonly Regex[] ForbiddenPromptPatterns =
    [
        // Korean text / UI element patterns
        new(@"Korean\s+text", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("한국어", RegexOptions.Compiled),
        new("한글", RegexOptions.Compiled),
        new(@"headline\s+text", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"button\s+text", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"CTA\s+button", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("accordion", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"sticky\s+footer", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"FAQ\s+card", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"trust\s+card", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"benefit\s+chip", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"text\s+in\s+Korean", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Top\s+part.*Middle\s+part", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // Composite / multi-scene patterns
        new(@"triptych[- ]style", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("collage", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"multiple\s+moments", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"before\s+and\s+after\s+in\s+one", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"split\s+scene", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new("infographic", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"label\s+callout", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"UI\s+mockup", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"poster\s+layout", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"catalog\s+grid", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"comic\s+panel", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"storyboard\s+frame", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"multi[- ]angle\s+composition", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"editorial\s+collage", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    // ─── Blueprint Tool Schema ──────────────────────────────────

    static readonly object BlueprintToolSchema = new
    {
        type = "object",
        properties = new
        {
            pageDesignSystem = new
            {
                type = "object",
                description = "Page-level design tokens inherited by all blocks.",
                properties = new
                {
                    mood = new { type = "string", description = "Overall emotional atmosphere" },
                    brandColors = new { type = "array", items = new { type = "string" }, description = "Ordered hex codes — primary first" },
                    backgroundApproach = new { type = "string", description = "Page-wide background strategy" },
                    typographyScale = new { type = "string", description = "Base typographic scale" }
                },
                required = new[] { "mood", "brandColors", "backgroundApproach", "typographyScale" }
            },
            visualBlocks = new
            {
                type = "array",
                description = "Ordered visual blocks mapping storyboard sections to layout structures.",
                minItems = 1,
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        blockId = new { type = "string", description = "Unique block identifier" },
                        blockType = new
                        {
                            type = "string",
                            @enum = new[] { "hero", "vertical-triptych", "value-benefit", "comparison-split", "proof-trust", "benefit-grid", "timeline", "offer-reassurance-sticky" },
                            description = "Block type from the defined vocabulary"
                        },
                        sectionRefs = new { type = "array", items = new { type = "string" }, description = "Storyboard section titles mapped to this block" },
                        heightWeight = new
                        {
                            type = "string",
                            @enum = new[] { "xl", "large", "medium", "short" },
                            description = "Viewport height weight"
                        },
                        layoutVariant = new { type = "string", description = "Layout variant from the allowed set for the block type" },
                        panels = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    role = new { type = "string", description = "Semantic role of this panel" },
                                    heightRatio = new { type = "number", description = "Proportion of block height (0.0-1.0)" },
                                    contentType = new
                                    {
                                        type = "string",
                                        @enum = new[] { "copy-with-visual", "visual-only", "copy-only" },
                                        description = "Content strategy for this panel"
                                    }
                                },
                                required = new[] { "role", "heightRatio", "contentType" }
                            }
                        },
                        assetSlots = new
                        {
                            type = "array",
                            minItems = 1,
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    slotId = new { type = "string", description = "Unique slot identifier" },
                                    prompt = new { type = "string", minLength = 80, description = "Image generation prompt (min 80 chars, English only, SUBJECT→ENVIRONMENT→LIGHTING→MOOD→COMPOSITION→PALETTE→NEGATIVE SPACE)" },
                                    aspectRatio = new { type = "string", description = "Target aspect ratio (e.g., 4:5, 16:9, 1:1)" },
                                    panelRef = new { type = "string", description = "Reference to the panel this slot belongs to" },
                                    priority = new { type = "string", @enum = new[] { "high", "medium" }, description = "Generation priority" },
                                    negativeConstraints = new
                                    {
                                        type = "array",
                                        items = new { type = "string" },
                                        description = "Constraints for image generation (must include: no text, no UI elements, no buttons, no captions)"
                                    },
                                    imageUrl = new { type = "string", description = "Generated image URL (populated after asset generation)" }
                                },
                                required = new[] { "slotId", "prompt", "aspectRatio", "panelRef", "priority", "negativeConstraints" }
                            }
                        },
                        designOverrides = new
                        {
                            type = "object",
                            description = "Optional block-level overrides when this block genuinely departs from the page design system.",
                            properties = new
                            {
                                mood = new { type = "string" },
                                backgroundApproach = new { type = "string" },
                                typographyScale = new { type = "string" }
                            }
                        }
                    },
                    required = new[] { "blockId", "blockType", "sectionRefs", "heightWeight", "layoutVariant", "panels", "assetSlots" }
                }
            },
            assumptions = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Inferences made when input was ambiguous"
            }
        },
        required = new[] { "pageDesignSystem", "visualBlocks" }
    };
}
