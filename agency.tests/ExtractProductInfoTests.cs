using System.ClientModel;
using System.ClientModel.Primitives;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using OpenAI.Chat;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Integration-flavoured tests for <see cref="GptService.ExtractProductInfoAsync"/> that
/// exercise argument validation (no network I/O). These tests pin the public contract the
/// P5 orchestrator depends on so changes that break input invariants are caught before
/// a PR reaches the orchestrator.
/// </summary>
public class ExtractProductInfoTests
{
    const string TestSystemPrompt = "test system prompt";

    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    [Fact]
    public async Task ExtractProductInfoAsync_NullDocuments_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.ExtractProductInfoAsync(TestSystemPrompt, null!));
    }

    [Fact]
    public async Task ExtractProductInfoAsync_EmptyDocuments_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(TestSystemPrompt, []));
    }

    [Fact]
    public async Task ExtractProductInfoAsync_BlankDocumentId_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(TestSystemPrompt, [new("   ", "some text")]));

        Assert.Contains("non-empty id", ex.Message);
    }

    [Fact]
    public async Task ExtractProductInfoAsync_BlankDocumentText_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(TestSystemPrompt, [new("spec.pdf", "   ")]));

        Assert.Contains("empty text", ex.Message);
    }

    [Fact]
    public async Task ExtractProductInfoAsync_DuplicateDocumentIds_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(TestSystemPrompt,
            [
                new("spec.pdf", "A"),
                new("spec.pdf", "B")
            ]));

        Assert.Contains("Duplicate", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExtractProductInfoAsync_Throws_When_SystemPrompt_Is_Null_Or_Empty_Or_Whitespace(
        string? badPrompt)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        // and ArgumentException for empty/whitespace — accept any ArgumentException subclass.
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(badPrompt!, [new("spec.pdf", "body")]));
    }

    [Fact]
    public async Task ExtractProductInfoAsync_Passes_Injected_Prompt()
    {
        // Verifies that ExtractProductInfoAsync wires the caller-supplied system prompt
        // verbatim into the first ChatMessage passed to CompleteChatAsync. A regression
        // that hard-codes or replaces the prompt would fail here because the captured
        // system message would no longer match the injected string.
        const string injected = "My custom extraction prompt";
        var docs = new[] { new ProductInfoDocument("spec.pdf", "Product body text.") };

        IReadOnlyList<ChatMessage>? captured = null;

        var chatClient = Substitute.For<ChatClient>();
        chatClient
            .CompleteChatAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatCompletionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.ArgAt<IEnumerable<ChatMessage>>(0).ToList();
                // Return a minimal Stop completion so ParseProductInfoResult receives empty content
                // and returns null — that's fine; we only care about the captured messages.
                var stopJson = """
                    {
                      "id": "chatcmpl-capture",
                      "object": "chat.completion",
                      "created": 1700000000,
                      "model": "gpt-5.4-nano",
                      "choices": [{
                        "index": 0,
                        "message": {"role": "assistant", "content": ""},
                        "finish_reason": "stop"
                      }],
                      "usage": {"prompt_tokens": 10, "completion_tokens": 1, "total_tokens": 11}
                    }
                    """;
                var completion = ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(stopJson))!;
                return Task.FromResult(ClientResult.FromValue(completion, new FakeProductInfoPipelineResponse()));
            });

        var svc = new ControlledProductInfoGptService(chatClient);
        await svc.ExtractProductInfoAsync(injected, docs);

        Assert.NotNull(captured);
        Assert.True(captured!.Count >= 1, "Expected at least one ChatMessage.");
        // Index 0 is the system message; its first content part must carry the injected prompt verbatim.
        var systemMsg = captured[0];
        Assert.Equal(injected, systemMsg.Content[0].Text);
    }

    [Fact]
    public void ProductInfoResult_RecordEquality_Works()
    {
        // Records support per-member equality; scalar / reference-type members compare as
        // expected. See the <remarks> block on ProductInfoResult — array-typed members use
        // reference equality, so we only assert scalar equality here.
        var a = new ProductInfoResult(
            1,
            new ProductInfoField<string>("Acme", "spec.pdf"),
            null, null, null, null, null, null, null,
            ["spec.pdf"]);

        var b = new ProductInfoResult(
            1,
            new ProductInfoField<string>("Acme", "spec.pdf"),
            null, null, null, null, null, null, null,
            ["spec.pdf"]);

        Assert.Equal(a.ProductName, b.ProductName);
        Assert.Equal(a.SchemaVersion, b.SchemaVersion);
    }

    // ---------------------------------------------------------------------------------------
    //  Regression coverage for PR #62 review feedback
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ParseProductInfoResult_IdWithHtmlChars_RoundTripsWithoutFalseNull()
    {
        // BLOCKER regression: previously doc.Id was wrapped in <user_input>…</user_input>
        // before being sent to the model, causing the model's echoed sourceDocument to fail
        // knownIds.Contains and every field to be nulled out. We now only HTML-escape the id
        // at prompt-build time (not at parse time) — so if the model correctly echoes the raw
        // id (including <>& chars), ValidateField must accept it.
        var rawId = "<weird>&id";
        var json = """
            {
              "schemaVersion": 1,
              "productName":  { "value": "Acme", "sourceDocument": "<weird>&id" },
              "sourceDocuments": ["<weird>&id"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json, [new ProductInfoDocument(rawId, "body")]);

        Assert.NotNull(result);
        Assert.NotNull(result!.ProductName);
        Assert.Equal("Acme", result.ProductName!.Value);
        Assert.Equal(rawId, result.ProductName.SourceDocument);
        Assert.NotNull(result.SourceDocuments);
        Assert.Contains(rawId, result.SourceDocuments!);
    }

    [Fact]
    public void ParseProductInfoResult_UnknownSourceDocuments_AreFilteredOut()
    {
        // P2 regression: model-returned sourceDocuments must be intersected with the canonical
        // input id list. Unknown ids get dropped. If the intersection is empty, we fall back
        // to the known id list so the provenance roster is never empty.
        var json = """
            {
              "schemaVersion": 1,
              "productName": { "value": "Acme", "sourceDocument": "spec.pdf" },
              "sourceDocuments": ["spec.pdf", "hallucinated.pdf", "<user_input>spec.pdf</user_input>"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json,
        [
            new ProductInfoDocument("spec.pdf",   "A"),
            new ProductInfoDocument("brochure.md","B")
        ]);

        Assert.NotNull(result);
        Assert.NotNull(result!.SourceDocuments);
        Assert.Equal(["spec.pdf"], result.SourceDocuments);
    }

    [Fact]
    public void ParseProductInfoResult_AllUnknownSourceDocuments_FallsBackToKnownIds()
    {
        // Fail-safe: if every id the model returned is unknown, we must fall back to the
        // canonical input list rather than handing callers an empty roster.
        var json = """
            {
              "schemaVersion": 1,
              "sourceDocuments": ["totally.bogus", "also.bogus"]
            }
            """;

        var result = _sut.ParseProductInfoResult(json,
        [
            new ProductInfoDocument("spec.pdf",   "A"),
            new ProductInfoDocument("brochure.md","B")
        ]);

        Assert.NotNull(result);
        Assert.Equal(["spec.pdf", "brochure.md"], result!.SourceDocuments);
    }

    // ─── Helpers for prompt-capture test ─────────────────────────────────────

    /// <summary>
    /// A <see cref="GptService"/> subclass that overrides <see cref="GetChatClient"/>
    /// to return the injected <see cref="ChatClient"/> substitute, allowing
    /// <see cref="ExtractProductInfoAsync_Passes_Injected_Prompt"/> to exercise
    /// the REAL production method without touching the OpenAI network.
    /// </summary>
    sealed class ControlledProductInfoGptService(ChatClient chatClient)
        : GptService(NullLogger<GptService>.Instance, "test-key")
    {
        internal override ChatClient GetChatClient(string model) => chatClient;
    }

    /// <summary>Minimal <see cref="PipelineResponse"/> stub required by <see cref="ClientResult.FromValue{T}"/>.</summary>
    sealed class FakeProductInfoPipelineResponse : PipelineResponse
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
}
