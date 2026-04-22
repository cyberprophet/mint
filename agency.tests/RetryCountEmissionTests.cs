using System.ClientModel;
using System.ClientModel.Primitives;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using OpenAI.Chat;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Tests that <see cref="ApiUsageEvent.RetryCount"/> is populated by every retry
/// loop that emits usage telemetry (Blueprint, DesignHtml, Storyboard). Before
/// Intent 037 Phase A these three paths never wrote <c>RetryCount</c>, so P5
/// analytics could not surface how often a provider call retried before
/// succeeding. Closes #85.
///
/// The OpenAI network call is intercepted via a subclass override
/// (see <see cref="ControlledGptService"/>) so tests run offline. Placeholder
/// prompts are used throughout per ADR-013 — no real prompt content.
/// </summary>
public class RetryCountEmissionTests
{
    // ─── Fixtures ─────────────────────────────────────────────────────────────

    const string PlaceholderSystemPrompt = "test-prompt";

    static readonly BlueprintContext BlueprintCtx = new(
        StoryboardJson: "{\"sections\":[]}",
        VisualDna: null,
        BriefJson: null,
        Feedback: null);

    static readonly StoryboardContext StoryboardCtx = new(
        Brief: "{\"productName\":\"TestProduct\"}",
        MarketContext: "{\"categoryInsights\":\"test\"}",
        VisualDna: null,
        TargetLanguage: "en",
        ForbiddenCliches: null,
        ProductType: "digital",
        Feedback: null);

    static DesignHtmlContext MakeDesignCtx() => new(
        Blueprint: MakeValidBlueprint(),
        Storyboard: MakeValidStoryboard(),
        Brief: null,
        Feedback: null);

    // ─── Blueprint: RetryCount on success-first-try ───────────────────────────

    [Fact]
    public async Task Blueprint_SuccessOnFirstTry_EmitsRetryCountZero()
    {
        var chatClient = BuildSequencedChatClient([
            MakeToolCallCompletion("call_1", "generate_layout_blueprint", ValidBlueprintJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateBlueprintAsync(
            PlaceholderSystemPrompt, BlueprintCtx, onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Single(events);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal("blueprint", events[0].Purpose);
    }

    [Fact]
    public async Task Blueprint_SuccessOnSecondAttempt_EmitsRetryCountZeroThenOne()
    {
        // First attempt returns malformed JSON (triggers retry); second succeeds.
        var chatClient = BuildSequencedChatClient([
            MakeToolCallCompletion("call_1", "generate_layout_blueprint", "{ this is not valid json {{{{"),
            MakeToolCallCompletion("call_2", "generate_layout_blueprint", ValidBlueprintJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateBlueprintAsync(
            PlaceholderSystemPrompt, BlueprintCtx, onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].RetryCount); // first try — not retried yet
        Assert.Equal(1, events[1].RetryCount); // second try — one retry occurred
    }

    [Fact]
    public async Task Blueprint_AllThreeAttemptsFail_EmitsRetryCountsZeroOneTwo()
    {
        var chatClient = BuildSequencedChatClient([
            MakeStopCompletion("call_1", "plain text 1"),
            MakeStopCompletion("call_2", "plain text 2"),
            MakeStopCompletion("call_3", "plain text 3"),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateBlueprintAsync(
            PlaceholderSystemPrompt, BlueprintCtx, onUsage: events.Add);

        Assert.Null(result); // exhausted retries
        Assert.Equal(3, events.Count);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal(1, events[1].RetryCount);
        Assert.Equal(2, events[2].RetryCount);
    }

    // ─── Storyboard: RetryCount on success-first-try ──────────────────────────

    [Fact]
    public async Task Storyboard_SuccessOnFirstTry_EmitsRetryCountZero()
    {
        var chatClient = BuildSequencedChatClient([
            MakeToolCallCompletion("call_1", "save_storyboard", ValidStoryboardJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateStoryboardAsync(
            PlaceholderSystemPrompt, StoryboardCtx, onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Single(events);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal("storyboard", events[0].Purpose);
    }

    [Fact]
    public async Task Storyboard_SuccessOnSecondAttempt_EmitsRetryCountZeroThenOne()
    {
        var chatClient = BuildSequencedChatClient([
            MakeToolCallCompletion("call_1", "save_storyboard", "{ invalid json {{{{"),
            MakeToolCallCompletion("call_2", "save_storyboard", ValidStoryboardJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateStoryboardAsync(
            PlaceholderSystemPrompt, StoryboardCtx, onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal(1, events[1].RetryCount);
    }

    [Fact]
    public async Task Storyboard_AllThreeAttemptsFail_EmitsRetryCountsZeroOneTwo()
    {
        var chatClient = BuildSequencedChatClient([
            MakeStopCompletion("call_1", "a"),
            MakeStopCompletion("call_2", "b"),
            MakeStopCompletion("call_3", "c"),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateStoryboardAsync(
            PlaceholderSystemPrompt, StoryboardCtx, onUsage: events.Add);

        Assert.Null(result);
        Assert.Equal(3, events.Count);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal(1, events[1].RetryCount);
        Assert.Equal(2, events[2].RetryCount);
    }

    // ─── DesignHtml: RetryCount on success-first-try ──────────────────────────

    [Fact]
    public async Task DesignHtml_SuccessOnFirstTry_EmitsRetryCountZero()
    {
        var chatClient = BuildSequencedChatClient([
            MakeToolCallCompletion("call_1", "render_and_preview_design", ValidDesignHtmlJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateDesignHtmlAsync(
            PlaceholderSystemPrompt, MakeDesignCtx(), onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Single(events);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal("design", events[0].Purpose);
    }

    [Fact]
    public async Task DesignHtml_SuccessOnSecondAttempt_EmitsRetryCountZeroThenOne()
    {
        var chatClient = BuildSequencedChatClient([
            MakeStopCompletion("call_1", "plain text without tool call"),
            MakeToolCallCompletion("call_2", "render_and_preview_design", ValidDesignHtmlJson()),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateDesignHtmlAsync(
            PlaceholderSystemPrompt, MakeDesignCtx(), onUsage: events.Add);

        Assert.NotNull(result);
        Assert.Equal(2, events.Count);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal(1, events[1].RetryCount);
    }

    [Fact]
    public async Task DesignHtml_AllThreeAttemptsFail_EmitsRetryCountsZeroOneTwo()
    {
        var chatClient = BuildSequencedChatClient([
            MakeStopCompletion("call_1", "a"),
            MakeStopCompletion("call_2", "b"),
            MakeStopCompletion("call_3", "c"),
        ]);
        var svc = new ControlledGptService(chatClient);
        var events = new List<ApiUsageEvent>();

        var result = await svc.GenerateDesignHtmlAsync(
            PlaceholderSystemPrompt, MakeDesignCtx(), onUsage: events.Add);

        Assert.Null(result);
        Assert.Equal(3, events.Count);
        Assert.Equal(0, events[0].RetryCount);
        Assert.Equal(1, events[1].RetryCount);
        Assert.Equal(2, events[2].RetryCount);
    }

    // ─── Helpers: model builders for valid payloads ──────────────────────────

    static BlueprintResult MakeValidBlueprint()
    {
        var pds = new PageDesignSystem(
            Mood: "fresh and modern",
            BrandColors: ["#FFFFFF", "#000000"],
            BackgroundApproach: "dark studio cutout",
            TypographyScale: "display-xl, body-md");

        var slot = new AssetSlot(
            SlotId: "slot-1",
            Prompt: "High-key studio photo of a sleek white sneaker on a marble surface, natural side lighting, minimal shadows, clean composition, neutral white palette, generous negative space on right",
            AspectRatio: "4:5",
            PanelRef: "panel-1",
            Priority: "high",
            NegativeConstraints: ["no text", "no ui elements", "no buttons", "no captions"],
            ImageUrl: null);

        var panel = new LayoutPanel("main", 1.0, "copy-with-visual");

        var block = new VisualBlock(
            BlockId: "b1",
            BlockType: "hero",
            SectionRefs: ["Hero"],
            HeightWeight: "xl",
            LayoutVariant: "full-bleed-center",
            Panels: [panel],
            AssetSlots: [slot],
            DesignOverrides: null);

        return new BlueprintResult(pds, [block], null);
    }

    static StoryboardResult MakeValidStoryboard()
    {
        // Not directly used to construct the tool-call JSON, but is a valid object
        // for the DesignHtmlContext fixture.
        return new StoryboardResult(
            Sections: [
                new StoryboardSection(
                    Title: "Hero",
                    StrategicIntent: "introduce the product",
                    SectionType: "hero",
                    Blocks: [new StoryboardBlock("image", "test-prompt")]),
            ],
            CtaText: "Buy Now");
    }

    static string ValidBlueprintJson() =>
        """
        {
          "pageDesignSystem": {
            "mood": "fresh and modern",
            "brandColors": ["#FFFFFF", "#000000"],
            "backgroundApproach": "dark studio cutout",
            "typographyScale": "display-xl, body-md"
          },
          "visualBlocks": [{
            "blockId": "b1",
            "blockType": "hero",
            "sectionRefs": ["Hero"],
            "heightWeight": "xl",
            "layoutVariant": "full-bleed-center",
            "panels": [{"role": "main", "heightRatio": 1.0, "contentType": "copy-with-visual"}],
            "assetSlots": [{
              "slotId": "slot-1",
              "prompt": "High-key studio photo of a sleek white sneaker on a marble surface, natural side lighting, minimal shadows, clean composition, neutral white palette, generous negative space on right",
              "aspectRatio": "4:5",
              "panelRef": "panel-1",
              "priority": "high",
              "negativeConstraints": ["no text", "no ui elements", "no buttons", "no captions"]
            }]
          }]
        }
        """;

    static string ValidStoryboardJson() =>
        """
        {
          "sections": [
            {
              "title": "Hero",
              "strategicIntent": "introduce the product",
              "sectionType": "hero",
              "blocks": [
                {"type": "heading", "content": "Make every day count"},
                {"type": "image", "content": "Studio photo of the product against a clean backdrop with soft rim lighting, centered composition, neutral warm palette, generous negative space, product-focused professional shot"}
              ]
            },
            {
              "title": "FAQ",
              "strategicIntent": "resolve purchase anxieties",
              "sectionType": "faq",
              "blocks": [
                {"type": "text", "content": "Q1: How long does it last? A: A long time. Q2: How do I use it? A: Easily. Q3: Can I return it? A: Yes within 30 days."},
                {"type": "image", "content": "Minimalist close-up photo of the product used in a calm home environment, soft window light, neutral palette, balanced composition, generous negative space, clean documentary style"}
              ]
            }
          ],
          "ctaText": "Buy Now"
        }
        """;

    static string ValidDesignHtmlJson()
    {
        const string html = "<html><body><section><h1>Hero</h1></section></body></html>";
        return $"{{\"html\":\"{html}\"}}";
    }

    // ─── Helpers: ChatClient / ChatCompletion fakes ──────────────────────────

    /// <summary>
    /// Creates a mock <see cref="ChatClient"/> that returns completions in sequence.
    /// If the test consumes more completions than supplied, the last one is reused
    /// (mirrors <see cref="ReferenceLinkAnalystTests"/>' BuildSequencedChatClient).
    /// </summary>
    static ChatClient BuildSequencedChatClient(IReadOnlyList<ChatCompletion> completions)
    {
        var chatClient = Substitute.For<ChatClient>();
        var callIndex = 0;

        chatClient
            .CompleteChatAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var idx = callIndex < completions.Count ? callIndex : completions.Count - 1;
                callIndex++;
                var result = ClientResult.FromValue(completions[idx], new FakePipelineResponse());
                return Task.FromResult(result);
            });

        return chatClient;
    }

    static ChatCompletion MakeToolCallCompletion(string callId, string toolName, string argumentsJson)
    {
        var escapedArgs = argumentsJson
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        var json = $$"""
            {
              "id": "chatcmpl-{{callId}}",
              "object": "chat.completion",
              "created": 1700000000,
              "model": "gpt-5.4",
              "choices": [{
                "index": 0,
                "message": {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [{
                    "id": "{{callId}}",
                    "type": "function",
                    "function": {
                      "name": "{{toolName}}",
                      "arguments": "{{escapedArgs}}"
                    }
                  }]
                },
                "finish_reason": "tool_calls"
              }],
              "usage": {"prompt_tokens": 100, "completion_tokens": 200, "total_tokens": 300}
            }
            """;

        return ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(json))!;
    }

    static ChatCompletion MakeStopCompletion(string callId, string text)
    {
        var escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var json = $$"""
            {
              "id": "chatcmpl-{{callId}}",
              "object": "chat.completion",
              "created": 1700000000,
              "model": "gpt-5.4",
              "choices": [{
                "index": 0,
                "message": {"role": "assistant", "content": "{{escapedText}}"},
                "finish_reason": "stop"
              }],
              "usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150}
            }
            """;

        return ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(json))!;
    }

    /// <summary>Minimal <see cref="PipelineResponse"/> stub for wrapping <see cref="ClientResult{T}"/>.</summary>
    sealed class FakePipelineResponse : PipelineResponse
    {
        BinaryData? _content;
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => _content ??= BinaryData.FromString(string.Empty);
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Content);
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override void Dispose() { }
    }

    /// <summary>
    /// A <see cref="GptService"/> subclass that overrides <see cref="GptService.GetChatClient"/>
    /// to return the injected <see cref="ChatClient"/> substitute, allowing tests to
    /// exercise the real retry loops without touching the OpenAI network.
    /// </summary>
    sealed class ControlledGptService(ChatClient chatClient)
        : GptService(NullLogger<GptService>.Instance, "test-key")
    {
        internal override ChatClient GetChatClient(string model) => chatClient;
    }
}
