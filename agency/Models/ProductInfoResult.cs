namespace ShareInvest.Agency.Models;

/// <summary>
/// Structured product information extracted from one or more source documents by the librarian.
/// Every field is nullable — an absent field means "not present in any provided document"
/// (no hallucination). Each present field carries provenance to the source document that
/// supplied the value so callers (e.g., P5) can present attribution to the user.
/// </summary>
/// <param name="SchemaVersion">Schema version for forward compatibility. Current version is 1.</param>
/// <param name="ProductName">Primary product name. Null if not present in any document.</param>
/// <param name="OneLiner">Single-sentence pitch or tagline for the product.</param>
/// <param name="KeyFeatures">Bulleted list of key features / selling points extracted verbatim from the source.</param>
/// <param name="DetailedSpec">Detailed specification — materials, dimensions, ingredients, technical details, etc.</param>
/// <param name="Usage">How to use / instructions for use.</param>
/// <param name="Cautions">Warnings, cautions, contraindications, or safety notes.</param>
/// <param name="TargetCustomer">Who the product is for (persona, demographic, use case).</param>
/// <param name="SellingPoints">Marketing-oriented selling points distinct from raw feature bullets.</param>
/// <param name="SourceDocuments">Identifiers of all documents considered during extraction.</param>
public record ProductInfoResult(
    int SchemaVersion,
    ProductInfoField<string>? ProductName,
    ProductInfoField<string>? OneLiner,
    ProductInfoField<string[]>? KeyFeatures,
    ProductInfoField<string>? DetailedSpec,
    ProductInfoField<string>? Usage,
    ProductInfoField<string>? Cautions,
    ProductInfoField<string>? TargetCustomer,
    ProductInfoField<string[]>? SellingPoints,
    string[]? SourceDocuments);

/// <summary>
/// A single extracted product-info field together with the identifier of the document that
/// supplied the value. When multiple documents mention the same field, the librarian selects
/// the most detailed / authoritative value and records which document it came from.
/// </summary>
/// <param name="Value">The extracted value. Never null — if the field is not present in any
/// document, the containing <see cref="ProductInfoResult"/> property is null instead.</param>
/// <param name="SourceDocument">Identifier of the source document (filename, ID, or URL) that
/// supplied this value. Allows P5 to render provenance badges next to each field.</param>
public record ProductInfoField<T>(
    T Value,
    string SourceDocument);

/// <summary>
/// A single input document for product-info extraction.
/// </summary>
/// <param name="Id">Stable identifier used as <c>SourceDocument</c> in the output
/// (e.g., original filename, UUID, or URL). Must be unique within a single call.</param>
/// <param name="Text">Plain-text body of the document. Pre-processed / OCR'd upstream by P5.</param>
public record ProductInfoDocument(
    string Id,
    string Text);
