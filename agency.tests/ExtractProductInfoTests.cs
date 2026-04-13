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
        // Records must support structural equality so callers can dedupe / cache results.
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
}
