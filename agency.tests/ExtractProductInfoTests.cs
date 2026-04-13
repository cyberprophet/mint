using Microsoft.Extensions.Logging.Abstractions;

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
    readonly GptService _sut = new(NullLogger<GptService>.Instance, "test-key");

    [Fact]
    public async Task ExtractProductInfoAsync_NullDocuments_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.ExtractProductInfoAsync(null!));
    }

    [Fact]
    public async Task ExtractProductInfoAsync_EmptyDocuments_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync([]));
    }

    [Fact]
    public async Task ExtractProductInfoAsync_BlankDocumentId_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync([new("   ", "some text")]));

        Assert.Contains("non-empty id", ex.Message);
    }

    [Fact]
    public async Task ExtractProductInfoAsync_BlankDocumentText_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync([new("spec.pdf", "   ")]));

        Assert.Contains("empty text", ex.Message);
    }

    [Fact]
    public async Task ExtractProductInfoAsync_DuplicateDocumentIds_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ExtractProductInfoAsync(
            [
                new("spec.pdf", "A"),
                new("spec.pdf", "B")
            ]));

        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void DefaultProductInfoSystemPrompt_MentionsEveryRequiredField()
    {
        // Pin the no-hallucination contract plus the full field list. If the prompt drifts
        // and drops a field, this fails loudly instead of silently producing empty results.
        var prompt = GptService.DefaultProductInfoSystemPrompt;

        Assert.Contains("productName", prompt);
        Assert.Contains("oneLiner", prompt);
        Assert.Contains("keyFeatures", prompt);
        Assert.Contains("detailedSpec", prompt);
        Assert.Contains("usage", prompt);
        Assert.Contains("cautions", prompt);
        Assert.Contains("targetCustomer", prompt);
        Assert.Contains("sellingPoints", prompt);
        Assert.Contains("null", prompt);
        Assert.Contains("sourceDocument", prompt);
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
}
