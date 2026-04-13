using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using OpenAI.Chat;

using ShareInvest.Agency.Models;

namespace ShareInvest.Agency.OpenAI;

public partial class GptService
{
    /// <summary>
    /// Default system prompt used by <see cref="ExtractProductInfoAsync"/> when the caller does
    /// not supply one. Encodes the librarian extraction contract:
    /// <list type="bullet">
    /// <item>Return strict JSON matching <see cref="ProductInfoResult"/>.</item>
    /// <item>Every field is optional — return <c>null</c> if not present in any document.</item>
    /// <item>Never invent or hallucinate data; quote / paraphrase from the source only.</item>
    /// <item>For each included field, record which document it came from in <c>sourceDocument</c>.</item>
    /// <item>When multiple documents disagree, pick the most detailed/authoritative and record that source.</item>
    /// </list>
    /// </summary>
    public const string DefaultProductInfoSystemPrompt = """
        You are a meticulous product-information librarian. You will receive one or more
        source documents describing a product, each tagged with a document id. Extract the
        following structured fields into strict JSON:

          - productName         (string)
          - oneLiner            (string — single-sentence pitch / tagline)
          - keyFeatures         (string[] — bulleted key features / benefits)
          - detailedSpec        (string — materials, dimensions, ingredients, tech specs)
          - usage               (string — how to use / instructions)
          - cautions            (string — warnings, contraindications, safety notes)
          - targetCustomer      (string — persona, demographic, use case)
          - sellingPoints       (string[] — marketing-oriented selling points)

        RULES — follow exactly:

        1. If a field is NOT present in any of the supplied documents, return null for that
           field. DO NOT invent, infer beyond the text, or hallucinate plausible values.
        2. Each present field MUST be emitted as an object of the form
           {"value": <the value>, "sourceDocument": "<document id>"} where <document id>
           is exactly one of the ids supplied in the input.
        3. If multiple documents describe the same field, pick the single most detailed or
           authoritative source and record THAT document's id. Do not merge strings from
           different documents into one value.
        4. For list-valued fields (keyFeatures, sellingPoints), the value is an array of
           strings all drawn from the same chosen sourceDocument.
        5. Include a top-level "schemaVersion": 1 and
           "sourceDocuments": [<every input document id>, ...] listing every document
           you considered (including ones that contributed nothing).
        6. Respond with JSON ONLY — no prose, no markdown fences, no commentary.
        """;

    /// <summary>
    /// Extracts structured product information from one or more source documents.
    /// Every output field carries provenance (<see cref="ProductInfoField{T}.SourceDocument"/>)
    /// so the caller can render which document supplied each field. Missing fields return
    /// <see langword="null"/> rather than hallucinated values.
    /// </summary>
    /// <param name="documents">Source documents to extract from. At least one required.</param>
    /// <param name="systemPrompt">Optional override for the extraction prompt. Defaults to
    /// <see cref="DefaultProductInfoSystemPrompt"/>.</param>
    /// <param name="model">Chat model to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="onUsage">Optional callback invoked with token usage after the call.</param>
    /// <returns>Parsed <see cref="ProductInfoResult"/>, or <see langword="null"/> if the model
    /// produced no valid JSON.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="documents"/> is empty or
    /// contains a document with a blank id or blank text.</exception>
    public virtual async Task<ProductInfoResult?> ExtractProductInfoAsync(
        IReadOnlyList<ProductInfoDocument> documents,
        string? systemPrompt = null,
        string model = "gpt-5.4-nano",
        CancellationToken cancellationToken = default,
        Action<ApiUsageEvent>? onUsage = null)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
            throw new ArgumentException("At least one document is required.", nameof(documents));

        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var doc in documents)
        {
            if (doc is null)
                throw new ArgumentException("Documents must not be null.", nameof(documents));

            if (string.IsNullOrWhiteSpace(doc.Id))
                throw new ArgumentException("Every document must have a non-empty id.", nameof(documents));

            if (string.IsNullOrWhiteSpace(doc.Text))
                throw new ArgumentException($"Document '{doc.Id}' has empty text.", nameof(documents));

            if (!seenIds.Add(doc.Id))
                throw new ArgumentException($"Duplicate document id '{doc.Id}'.", nameof(documents));
        }

        var chatClient = GetChatClient(model);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 4096,
            Temperature = 0.1f
        };

        var userContent = new StringBuilder();
        userContent.AppendLine("Extract product information from the following documents.");
        userContent.AppendLine("Use each document's id exactly as shown when recording sourceDocument.");
        userContent.AppendLine();

        foreach (var doc in documents)
        {
            // Id must round-trip verbatim: model is told to echo it as sourceDocument, and
            // ValidateField rejects anything not in the canonical id set. Use the lighter
            // HTML-escape (no <user_input> wrapping) so the raw id survives the round trip.
            userContent.AppendLine($"<document id=\"{PromptSanitizer.EscapeIdentifierForPrompt(doc.Id)}\">");
            userContent.AppendLine(PromptSanitizer.EscapeForPrompt(doc.Text));
            userContent.AppendLine("</document>");
            userContent.AppendLine();
        }

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(systemPrompt ?? DefaultProductInfoSystemPrompt),
            ChatMessage.CreateUserMessage(userContent.ToString())
        };

        var sw = Stopwatch.StartNew();
        var result = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        sw.Stop();

        var completion = result.Value;

        if (onUsage is not null && completion.Usage is { } usage)
        {
            onUsage(new ApiUsageEvent("openai", model, usage.InputTokenCount, usage.OutputTokenCount,
                "product_info", LatencyMs: (int)sw.ElapsedMilliseconds));
        }

        var raw = completion.Content.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("Product-info extraction returned empty content");
            return null;
        }

        return ParseProductInfoResult(raw, documents);
    }

    /// <summary>
    /// Parses the raw model output into a <see cref="ProductInfoResult"/>.
    /// Strips markdown fences, tolerates PascalCase keys, and normalizes the schema version.
    /// Exposed as <see langword="internal"/> so unit tests can exercise parsing without hitting
    /// the network.
    /// </summary>
    internal ProductInfoResult? ParseProductInfoResult(
        string raw,
        IReadOnlyList<ProductInfoDocument>? documents = null)
    {
        var json = raw.Trim();

        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');

            if (firstNewline >= 0)
                json = json[(firstNewline + 1)..];

            if (json.EndsWith("```"))
                json = json[..^3];

            json = json.Trim();
        }

        ProductInfoResult? result;

        try
        {
            result = JsonSerializer.Deserialize<ProductInfoResult>(json, CaseInsensitiveOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse product-info result JSON");
            return null;
        }

        if (result is null)
            return null;

        // Normalize schemaVersion: a missing field deserializes to 0 — upgrade to 1.
        if (result.SchemaVersion == 0)
            result = result with { SchemaVersion = 1 };

        // Normalize sourceDocuments against the canonical input list:
        //   - If the model omitted / emptied the field, backfill from inputs.
        //   - If the model returned a set, intersect with known inputs to drop any
        //     hallucinated ids (model sometimes invents or echoes wrapper tags).
        //   - Fail-safe: if the intersection is empty, fall back to the known id list so
        //     callers always receive a non-empty provenance roster.
        if (documents is { Count: > 0 })
        {
            var canonicalIds = documents.Select(d => d.Id).ToArray();

            if (result.SourceDocuments is null or { Length: 0 })
            {
                result = result with { SourceDocuments = canonicalIds };
            }
            else
            {
                var knownSet = new HashSet<string>(canonicalIds, StringComparer.Ordinal);
                var filtered = result.SourceDocuments
                    .Where(id => !string.IsNullOrWhiteSpace(id) && knownSet.Contains(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                result = result with
                {
                    SourceDocuments = filtered.Length > 0 ? filtered : canonicalIds
                };
            }
        }

        // Validate that every present field cites a known document id, when we have the
        // input list to check against. Unknown citations are dropped (set to null) rather
        // than silently kept — this enforces the "no hallucination" rule.
        if (documents is { Count: > 0 })
        {
            var knownIds = new HashSet<string>(documents.Select(d => d.Id), StringComparer.Ordinal);

            result = result with
            {
                ProductName = ValidateField(result.ProductName, knownIds),
                OneLiner = ValidateField(result.OneLiner, knownIds),
                KeyFeatures = ValidateField(result.KeyFeatures, knownIds),
                DetailedSpec = ValidateField(result.DetailedSpec, knownIds),
                Usage = ValidateField(result.Usage, knownIds),
                Cautions = ValidateField(result.Cautions, knownIds),
                TargetCustomer = ValidateField(result.TargetCustomer, knownIds),
                SellingPoints = ValidateField(result.SellingPoints, knownIds)
            };
        }

        return result;
    }

    static ProductInfoField<T>? ValidateField<T>(ProductInfoField<T>? field, HashSet<string> knownIds)
    {
        if (field is null)
            return null;

        if (field.Value is null)
            return null;

        if (string.IsNullOrWhiteSpace(field.SourceDocument))
            return null;

        if (!knownIds.Contains(field.SourceDocument))
            return null;

        return field;
    }
}
