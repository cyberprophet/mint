# Changelog

All notable changes to **ShareInvest.Agency** are documented here.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

- NuGet: <https://www.nuget.org/packages/ShareInvest.Agency/>
- Primary consumer: **P5 — page-mint-server** (creative-server / vendo)

---

## [0.16.6] — 2026-04-24

### Fixed
- **Drop `ImageEditOptions.Quality` in `GenerateSingleShotAsync`** — OpenAI .NET SDK 2.10.0 serializes this property in a shape the `POST /v1/images/edits` endpoint rejects as `HTTP 400 (invalid_request_error: unknown_parameter: quality)`, despite OpenAI's REST API accepting `quality=high` on the same endpoint for the same model when the request is built by hand. Reproduced in an isolated .NET 10 probe on 2026-04-24 against `gpt-image-2`. Empirically, removing `Quality` while keeping `Size` + `OutputFileFormat` + `EndUserId` makes the call succeed; all other single-field probes (`Size` only, `OutputFileFormat` only, `Size+OutputFileFormat+EndUserId`) also succeed. Only the presence of `Quality` in the options bag triggers the reject.

### Known upstream issue
- Root cause lives in `OpenAI.Images.ImageEditOptions` in the OpenAI .NET SDK 2.10.0. The public generations endpoint (which StudioMint does not use) appears to accept the same property correctly, so the regression is specific to the edit path's serialization. Tracking for SDK upgrade when a fix lands. Until then, we rely on the server's default `quality=auto`, which is acceptable for the v1 4-cut output.

### Notes
- Purely behavioural — no API surface changes. P5 consumers do not need to touch their wiring beyond bumping the NuGet reference.

---

## [0.16.5] — 2026-04-24

### Fixed
- **Restore the 4-param `GptService` constructor as a binary-compatible overload** (Codex review feedback on 0.16.4). 0.16.4 replaced `GptService(ILogger<GptService>, string, string, string?)` with a 5-param variant that had an optional `imageGenerationModel = null` at the end. C# treats that as the same *source* signature but emits a different *IL* signature — assemblies compiled against ≤0.16.2 would therefore hit `MissingMethodException` at runtime when loaded with 0.16.4. The 5-param overload is retained (with the default value removed so the two don't overlap ambiguously); the old 4-param overload now delegates to it with `imageGenerationModel: null`, preserving the ≤0.16.2 behaviour byte-for-byte.

### Notes
- **NuGet consumers: bump `Models.csproj`** in P5 (creative-server) to `Version="0.16.5"`. 0.16.4 remains on nuget.org for history; use 0.16.5 or later to avoid the binary-compat trap for any downstream consumer that did not recompile.

---

## [0.16.4] — 2026-04-24

### Changed
- **Split the image model into two knobs** on `GptService`: `imageModel` (edit endpoint, StudioMint) and `imageGenerationModel` (generations endpoint, PageMint). Adds a new optional constructor parameter `imageGenerationModel` on both the API-key and `OpenAIClientOptions` overloads. Falls back to `imageModel` when null so old callers keep working without changes. Enables consumers (P5) to route StudioMint to `gpt-image-2` while keeping PageMint's high-volume generations on the cheaper `gpt-image-1-mini`.
- **Restore full `ImageEditOptions` set for StudioMint**: `Size = 1024x1024`, `Quality = High`, `OutputFileFormat = Png`, `EndUserId = userId`. 0.16.3 stripped these because `gpt-image-1` / `gpt-image-1-mini` rejected them on the public edit endpoint as `unknown_parameter`. Now that the expected edit model is `gpt-image-2`, which accepts the full set per the Azure Foundry spec for gpt-image-series, they're safe again.
- **Drop `SupportsFullEditOptions` gating** introduced mid-0.16.4 review. With the edit model pinned to `gpt-image-2`+ via the split, the model-aware fallback is no longer needed. Simpler and one fewer concept to carry.

### Known issue
- **Binary-compat regression** on the `GptService(ILogger, string, string, string?)` constructor: the signature was widened to 5 params with an optional `imageGenerationModel = null`, which changes the IL surface. Source-compatible, but assemblies compiled against ≤0.16.2 can hit `MissingMethodException` at runtime. **Fixed in 0.16.5.**

### Notes
- Superseded by 0.16.5 for the binary-compat issue above. Functionally correct otherwise.

---

## [0.16.3] — 2026-04-24

### Fixed
- **Drop unsupported `output_format` from `ImageEditOptions` in `GenerateSingleShotAsync`.** The OpenAI `gpt-image-1` public edit endpoint (`POST /v1/images/edits`) rejects `output_format` as `unknown_parameter`, unlike the generations endpoint that accepts it. The SDK's `ImageEditOptions` exposes the property on both paths, so the compiler can't catch the cross-endpoint leak. Empirically confirmed on 2026-04-24 via the first real-DB StudioMint run from `jim@mint.surf` — all 4 shots failed `HTTP 400 (invalid_request_error: unknown_parameter) Parameter: output_format`. Removing the assignment lets the edit endpoint use its PNG default, matching what the call site wanted anyway.

### Added
- **`ClassifyBadRequest(string?)` helper + `ErrorReason = "bad_request"`** for HTTP 400 responses that are *not* content-policy rejections. The previous catch block labeled every 400 as `"moderation"`, which masked this regression behind a user-safe-looking label. New logic matches `moderation_blocked` / `content_policy_violation` substrings (case-insensitive) and returns `"moderation"` only for genuine policy blocks; everything else (parameter shape, invalid image, etc.) now surfaces as `"bad_request"` in logs and the `StudioMintShot.ErrorReason` field.
- **6 unit tests covering the classifier**: null / empty / moderation_blocked / content_policy_violation / unknown_parameter (regression) / case-insensitive matching.

### Superseded
- **0.16.3 is retained on nuget.org for history but superseded by 0.16.4.** The edit options were stripped in 0.16.3 to work around gpt-image-1's rejection; 0.16.4 restores the full set after the image model is pinned to gpt-image-2.

---

## [0.16.2] — 2026-04-23

### Fixed
- **Drop `<AnalysisLevel>latest-Recommended</AnalysisLevel>` from `Agency.csproj`.** The broadened analyzer pack (CA1725, CA1848, CA1310, CA1822, CA1859, …) produced ~120 new warnings that P5's CI pipeline promoted to build errors via `dotnet build Vendo.slnx --no-restore -c Release --warnaserror`, breaking the 0.16.1 consumer deploy on run `24864278028`. The intent of 0.16.1 — promote CA2007 to error to prevent regression — is preserved via `.editorconfig` (`dotnet_diagnostic.CA2007.severity = error`), which enables the analyzer on its own without the broader rule set. Verified locally: `dotnet build -c Release --warnaserror` clean with 0.16.2; deliberately removing `.ConfigureAwait(false)` still triggers `CA2007` as error.

### Notes
- **NuGet consumers: bump `Models.csproj`** in P5 to `Version="0.16.2"` and redeploy. 0.16.1 is retained on nuget.org for history but should not be used because it fails P5's `--warnaserror` CI build.

---

## [0.16.1] — 2026-04-23

### Security
- **`GeminiProvider` prompt-sanitizer parity with OpenAI path** (P0 sweep #92). `Google/GeminiProvider.cs` now wraps user-controlled fields with `PromptSanitizer.EscapeForPrompt` (document text, URL, target language, product name, truncated HTML) and `PromptSanitizer.EscapeIdentifierForPrompt` (document IDs) in `ExtractProductInfoAsync` + `AnalyzeReferenceLinkAsync`, matching the OpenAI `GptService.ProductInfo` hardening shipped in 0.14.0 (Intent 043). A prompt-injection payload in a linked document or reference URL now breaks out of the Gemini path no more easily than the OpenAI path.

### Changed
- **`ConfigureAwait(false)` applied to every `await` in the library** (P0 sweep #93). 32 call sites across 12 files: `GptService.cs`, `GptService.Research.cs`, `GptService.Blueprint.cs`, `GptService.DesignHtml.cs`, `GptService.Storyboard.cs`, `GptService.Vision.cs`, `GptService.ReferenceLink.cs`, `GptService.StudioMint.cs`, `GptService.ProductInfo.cs`, `GptService.Image.cs`, `WebTools.cs`, `FallbackSearchProvider.cs`, `Google/GeminiProvider.cs`. This follows CA2007 guidance for public-library authoring and removes deadlock risk for callers in non-ASP.NET-Core hosts (WinForms, WPF, ASP.NET Framework, sync-over-async).
- **`CA2007` promoted to compile-time error.** `.editorconfig` now sets `dotnet_diagnostic.CA2007.severity = error` and `Agency.csproj` adopts `<AnalysisLevel>latest-Recommended</AnalysisLevel>` so any future `await` missing `.ConfigureAwait(false)` fails the build.

### Fixed
- **`GenerateTitleAsync` null-guard** on both `GptService` and `GeminiProvider` (P0 sweep #94 — P7-04). `conversationText` is now validated with `ArgumentException.ThrowIfNullOrWhiteSpace`, matching the existing guard pattern used on other prompt parameters. Previously callers would get a cryptic `NullReferenceException` during string interpolation instead of a clean argument error.
- **`ApiUsageEvent` provider name now uses `ProviderName`** (P0 sweep #94 — P7-07). `GptService.ReferenceLink.cs:87` and `GptService.StudioMint.cs:120` were hardcoding `new ApiUsageEvent("openai", ...)` regardless of the active provider, corrupting billing/analytics aggregation when the caller was configured for Gemini or any other provider. Both sites now read `this.ProviderName`.

### Notes
- **NuGet consumers: bump `Models.csproj`** in P5 (creative-server) from `<PackageReference Include="ShareInvest.Agency" Version="0.16.0" />` to `Version="0.16.1"` and redeploy. The `ConfigureAwait(false)` rollout is source-compatible; no API surface changes.
- **Tests**: `HardeningP0Sweep20260423Tests.cs` covers sanitizer delimiter presence in both Gemini entry points, `ArgumentException` throws for null/empty/whitespace title inputs on both providers, and `ApiUsageEvent.ProviderName` propagation.

---

## [0.16.0] — 2026-04-22

### Added
- **`StudioMintShotDefinition` promoted from `internal` to `public`** (Intent 038 Phase B PR-A). P5 can now construct shot definitions from its own MD files (`shot-cutout.md` / `shot-styled.md` / `shot-detail.md` / `shot-special.md`) and pass them directly to `GenerateStudioMintAsync` without depending on the internal v1 defaults.
- **`IReadOnlyList<StudioMintShotDefinition>? shots` optional parameter added to `GenerateStudioMintAsync`**, placed **last** in the parameter list (after `cancellationToken` and `onUsage`) so existing positional callers — `GenerateStudioMintAsync(basePrompt, request, ct)` — continue to compile and bind `ct` to the `cancellationToken` slot. When `null` (or omitted), the method falls back to the internal `StudioMintShotTypes.All` v1 list for backward compatibility. P5 should always pass an explicit shot list after adopting 0.16.0; the fallback will be removed in 0.17.0 once all consumers migrate.
- **`StudioMintShotDefinition` constructor validation.** `Id`, `Label`, and `Direction` are each guarded with `ArgumentException.ThrowIfNullOrWhiteSpace`, so external consumers cannot ship malformed shot definitions that would silently corrupt the generated prompt.

### Changed
- `StudioMintShotTypes.All` remains `internal`. P5 does not reference it directly; it passes its own shot definitions.

### Notes
- **Breaking change surface is zero for existing callers.** The new optional `shots` parameter is last in the signature, so both positional calls (`..., ct)`) and named-arg calls (`cancellationToken: token`) continue to bind correctly.
- The rev.3 industry 4-cut IDs (`cutout` / `styled` / `detail` / `special`) replace the v1 IDs (`hero-front` / `lifestyle` / `detail-macro` / `alt-angle`) at the P5 level; P7 is agnostic to the specific IDs.

### Tests
- **`StudioMintShotsParameterTests.cs`** covers public visibility of the record, external construction, record equality, `BuildShotPrompt` field flow for custom shots, null-shots fallback validation, empty-list behavior, `StudioMintResult` vacuous-complete for zero shots, rev.3 industry pack shape assertions (`[Theory]` over all 4 cut slots), constructor-validation for each of `Id` / `Label` / `Direction` (`[Theory]` with null / empty / whitespace), and a positional-call regression test (`..., ct)` form). Suite: 618 → 684 passing.

---

## [0.15.5] — 2026-04-22

### Fixed
- **`ApiUsageEvent.RetryCount` is now populated by every retry loop that emits usage telemetry.** `GptService.GenerateBlueprintAsync`, `GptService.GenerateDesignHtmlAsync`, and `GptService.GenerateStoryboardAsync` previously emitted usage events with `RetryCount = null`, so P5 analytics had no signal for how often a provider call retried before succeeding. Each emitted event now carries the 0-indexed retry count for the attempt that produced it: `RetryCount = 0` means the first attempt, `RetryCount = 1` means one prior retry occurred, and so on (up to `maxRetries - 1`). Unblocks Intent 037 Phase A retry analytics. Closes #85.

### Tests
- **`RetryCountEmissionTests.cs`** adds 9 tests × 3 scenarios (first-try success, second-try success after retry, all-three-attempts-fail) across Blueprint, DesignHtml, and Storyboard. Each test drives an NSubstitute-backed `ChatClient` through the real retry loop and asserts the full `RetryCount` sequence on the emitted events. Placeholder `"test-prompt"` strings only per ADR-013 — no real prompt content embedded. Suite: 609 → 618 passing.

---

## [0.15.4] — 2026-04-22

### Fixed
- **Prompt-ownership guardrail (ADR-013) applied to three missed entry points.** `GptService.Blueprint*`, `GptService.DesignHtml*`, and `GptService.Storyboard*` now validate their `systemPrompt` parameter with `ArgumentException.ThrowIfNullOrWhiteSpace` at method entry, matching the six peer methods (`GenerateTitleAsync`, `AnalyzeImageAsync`, `ResearchProductAsync`, and Gemini counterparts). Previously null / empty / whitespace prompts would be forwarded to the provider instead of failing fast on the consumer side. Closes #83.

### Tests
- **`PromptValidationTests.cs`** gains three `[Theory]` methods × 3 `InlineData` cases each covering null / `""` / `"   "` for Blueprint, DesignHtml, Storyboard. Placeholder prompt strings only per ADR-013 — no real prompt content embedded. Suite: 600 → 609 passing.

---

## [0.15.3] — 2026-04-21

### Fixed
- **Image calls can now be priced accurately per OpenAI's published dual-rate model** (supersedes 0.15.1/0.15.2 which only republished the missing `gpt-image-1-mini` entry). OpenAI bills two separate input rates per image model — text-input (prompt tokens) and image-input (source-image tokens for edits) — which 0.15.0/0.15.1's single-bucket `InputUsdPer1M` could not distinguish, so every image call was either over- or under-charged depending on the flow. See **Changed** below for the new schema.

### Changed
- **`ModelPricing` record gains `ImageInputUsdPer1M` and `ImageCacheReadUsdPer1M`** (both nullable `decimal?`, default null). Text models leave them null; image models fill both buckets. Also reorders existing positional params — `CacheWriteUsdPer1M` / `CacheReadUsdPer1M` kept in the same slots so Anthropic call sites don't break.
- **`EstimateCost(provider, model, inputTokens, outputTokens, ...)` gains `imageInputTokens` and `imageCacheReadTokens` optional parameters.** When present, the image rates are multiplied with those token counts and added to the total. Omitting them preserves pre-0.15.2 behavior for text-model callers.
- **`ApiUsageEvent` gains `ImageInputTokens` and `ImageCacheReadTokens`** (both nullable int). `InputTokens` is now semantically the **text-input** portion for image models (source-image tokens go in `ImageInputTokens`). Text models are unchanged.
- **`GptService.Image.GenerateImageAsync` and `GptService.StudioMint.GenerateImageEditsAsync` read OpenAI's `ImageInputTokenUsageDetails.{TextTokenCount, ImageTokenCount}`** and populate `ApiUsageEvent.InputTokens` / `ApiUsageEvent.ImageInputTokens` separately.
- **Pricing table (image models) re-verified 2026-04-21 against OpenAI's public pricing page:**

  | Model              | Text input | Image input | Output | Cached text | Cached image |
  |--------------------|------------|-------------|--------|-------------|--------------|
  | `gpt-image-1`      | $5.00      | $10.00      | $40.00 | $1.25       | $2.50        |
  | `gpt-image-1.5`    | $5.00      | $8.00       | $32.00 | $1.25       | $2.00        |
  | `gpt-image-1-mini` | $2.00      | $2.50       | $8.00  | $0.20       | $0.25        |

  All rates are USD per 1M tokens.
- **`PricingVersion` 3 → 4.**

### Notes
- `ApiUsageLog` (P5 schema) remains unchanged — the text/image split is computed at write time into `EstimatedCostUsd`, not stored. `InputTokens` on existing image rows is combined (old semantics); on new image rows it becomes text-only, which only matters if someone queries per-token breakdowns (no current consumer does). A proper schema-level split would require a DB migration and is out of scope here.

---

## [0.15.2] — 2026-04-21 (superseded by 0.15.3)

Repack of 0.15.1 with the version field bumped; same single-bucket schema. Superseded before any consumer could adopt it.

## [0.15.1] — 2026-04-21 (superseded by 0.15.3)

Initial republish attempt that only corrected the "package already exists" skip from 2026-04-17's e962f80 commit; shipped the single-bucket image entries unchanged and used $10 input as a conservative approximation of OpenAI's dual rates. 0.15.3 supersedes with proper text-input / image-input separation.

---

## [0.15.0] — 2026-04-17

### Added
- **`ModelPricingTable` + `ModelPricing`** — static per-model token pricing lookup, moved from P5 `Server/Core/` to the shared Agency library (`ShareInvest.Agency.Models`). Enables any consumer to estimate API costs consistently. Prices are sourced from provider public docs and updated manually; bump `PricingVersion` when entries change.

---

## [0.14.0] — 2026-04-17

### Removed
- **`GptService.DefaultProductInfoSystemPrompt` const deleted.** The last ADR-013 holdout: the inline system prompt for product-info extraction no longer ships in the public NuGet. P5 owns and injects it at the call site.

### Changed
- **BREAKING: `GptService.ExtractProductInfoAsync` now requires `string systemPrompt` as the first parameter.** The previously optional `string? systemPrompt = null` trailing parameter has been promoted to a required first positional parameter. Callers must supply a non-null, non-whitespace prompt. Fixes [mint#74](https://github.com/cyberprophet/mint/issues/74).

---

## [0.13.0] — 2026-04-16

### Removed
- **`agency/Prompts/` folder deleted entirely.** The four MD files (`librarian-system.md`, `studio-mint-base.md`, `title-system.md`, `visual-dna-system.md`) no longer ship in the public repository or the NuGet package. Prompts live in P5 (`Models/Prompts/`) and are injected at the call site.
- `BuildReferenceLinkSystemPrompt()` removed from `GptService.ReferenceLink`.
- `LoadBasePrompt()` helper + embedded-resource resolution removed from `GptService.StudioMint`.

### Changed
- **BREAKING: `GptService.AnalyzeReferenceLinkAsync` now requires `string systemPrompt` as the first parameter.**
- **BREAKING: `GptService.GenerateStudioMintAsync` now requires `string basePrompt` as the first parameter.**
- Both follow the existing injection pattern used by `BlueprintAsync`, `DesignHtmlAsync`, `StoryboardAsync`, `ResearchAsync`, `GenerateTitleAsync`, and `ExtractProductInfoAsync`. Keeps the public NuGet surface free of product-specific agent prompts.
- Package version bumped `0.11.0 → 0.13.0` (0.12.0 was pushed earlier today with only the reference-link change and is superseded).

---

## [0.12.0] — 2026-04-16

### Changed
- **BREAKING: `GptService.AnalyzeReferenceLinkAsync` now requires `string systemPrompt` as the first parameter.** Superseded by 0.13.0 — prefer the newer release, which also removes the `agency/Prompts/` folder.
- Package version bumped `0.11.0 → 0.12.0`.

---

## [0.11.0] — 2026-04-16

### Added
- **`AnalyzeReferenceLinkAsync` on `GptService`** (Intent 041 Phase A) — dedicated subagent that extracts layout pattern, copy tone, color palette, typography, messaging angles, and raw summary from a reference web page's HTML. Uses `gpt-5.4` with tool-calling + JSON schema validation and 3-attempt retry. ([#70])
- **`ReferenceLinkAnalysis` DTO** (Intent 041 Phase A) — structured result record (`LayoutPattern`, `CopyTone`, `ColorPalette`, `TypographyStyle`, `MessagingAngles`, `RawSummary`). ([#70])
- **`ReferenceLinkContext`** — input context (target language + optional product name) for the reference-link analyzer.

### Changed
- Package version bumped `0.10.0 → 0.11.0`.

---

## [0.10.0] — 2026-04-13

### Added
- **Studio Mint 4-shot image-edit agent** (Intent 031) — multi-pass image editing workflow for the studio-mint surface. ([#65])

### Tests
- Exhaustive coverage pass across the package; fixed `EmbeddedResourceTests`. ([#64])
- Closed coverage gaps in storyboard generation, Athena design HTML, image mapping, and prompt assembly. ([#63])

### Changed
- Package version bumped `0.9.1 → 0.10.0`. ([#66])

---

## [0.9.1] — 2026-04

### Added
- **Librarian structured product-info extraction** (Intent 027). ([#62])

### Fixed
- Enforce hard character cap including truncation-marker length in `web_fetch` output (P-07). ([#58])

---

## [0.9.0] — 2026-04

### Fixed
- Cap `web_fetch` tool result at 8,000 chars with an explicit truncation marker (P-07). ([#57])
- Map HTTP 401 and 429 correctly in image-generation error handling (E-21). ([#56])
- Log primary search-provider failures instead of swallowing them silently (E-20). ([#55])
- Pin test-project package versions and add missing explicit references (E-19). ([#54])

---

## [0.8.0] — 2026-03

### Added
- SSRF security hardening and expanded model coverage. ([#40])
- Research-loop and blueprint-gate validation tests. ([#46])
- `WebToolsHtmlExtractionTests` and `FallbackSearchProviderTests`.

### Fixed
- Implement `IDisposable` on `GptService` to prevent socket leaks (E-16). ([#43])
- Escape user input in LLM prompts to prevent injection (S-12). ([#47])
- Move Exa API key from URL query string to `x-api-key` header (S-07). ([#45])
- Remove dead `OpenCodeService` that crashed on empty URI (E-17). ([#44])
- Deny requests on DNS-resolution failure in SSRF check. ([#39])

---

## [0.7.0] — 2026-03

### Added
- **Gate 8** — required-section validation (`faq`, `spec-table`). ([#38])
- Block-type diversity and `layoutVariant` non-repetition gates (Gates 14–15).
- Athena center-alignment overuse warning gate.
- Apollo `sectionType` enum expansion: `faq`, `spec-table`, `how-to-use`, `certification`.

### Fixed
- Skip the 30% `blockType` cap for pages with fewer than 5 blocks.
- Resolve conflict between Gate 13 (background cycling) and Gates 14–15.
- Address review feedback for blueprint gates (#34).
- Read actual token usage from the image-generation API response. ([#32])
- Remove `NonBacktracking` from sanitize regexes that use lookahead. ([#31])

### Changed
- **Athena HTML design generation** added to the agency. ([#30])
- Externalize system prompts from the public NuGet package. ([#28])
- Blueprint models for Pygmalion migration. ([#27])
- P0 resilience improvements: observability, data quality, test coverage. ([#26])

---

## [0.2.0] — 2025 (initial feature release)

### Added
- **Title generation agent** via `GenerateTitleAsync` — uses `gpt-5-nano` with embedded
  PageMint-tailored system prompt; titles capped at 50 characters.
- Storyboard generation service (Apollo → P7 migration). ([#22])
- Visual DNA image analysis via OpenAI Vision API. ([#7])
- Research fetch failure budget, per-URL retry limit, and Cloudflare retry. ([#14])
- Google.GenAI package with `GeminiService` and `OpenCodeService` scaffolding.

### Fixed
- Tailor title prompt to PageMint design-agency context.
- Make `GenerateTitleAsync` virtual for testability.
- Guard empty `Content` array and align truncation to 50-char limit.
- Increase `MaxOutputTokenCount` to 1024 for `gpt-5-nano`.
- Remove `temperature` parameter unsupported by `gpt-5-nano`.
- Strengthen target-language enforcement in storyboard generation. ([#25])
- Per-product `forbiddenCliches` validation in storyboard. ([#23])

---

## [0.1.1] — 2025

### Fixed
- Align `.csproj` version to published NuGet baseline (0.1.0 → 0.1.1). ([#3])
- Use PNG icon for the NuGet package (ICO not supported). ([#1])

---

## [0.1.0] — 2025 (first publish)

### Added
- Initial **Agency Library** with NuGet package configuration and CI pipeline.
- `nuget.org` publish step.
- Re-export `GeneratedImageQuality` via `ImageQuality` wrapper.

---

[0.10.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.10.0
[0.9.1]: https://www.nuget.org/packages/ShareInvest.Agency/0.9.1
[0.9.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.9.0
[0.8.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.8.0
[0.7.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.7.0
[0.2.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.2.0
[0.1.1]: https://www.nuget.org/packages/ShareInvest.Agency/0.1.1
[0.1.0]: https://www.nuget.org/packages/ShareInvest.Agency/0.1.0

[#66]: https://github.com/cyberprophet/mint/pull/66
[#65]: https://github.com/cyberprophet/mint/pull/65
[#64]: https://github.com/cyberprophet/mint/pull/64
[#63]: https://github.com/cyberprophet/mint/pull/63
[#62]: https://github.com/cyberprophet/mint/pull/62
[#58]: https://github.com/cyberprophet/mint/pull/58
[#57]: https://github.com/cyberprophet/mint/pull/57
[#56]: https://github.com/cyberprophet/mint/pull/56
[#55]: https://github.com/cyberprophet/mint/pull/55
[#54]: https://github.com/cyberprophet/mint/pull/54
[#47]: https://github.com/cyberprophet/mint/pull/47
[#46]: https://github.com/cyberprophet/mint/pull/46
[#45]: https://github.com/cyberprophet/mint/pull/45
[#44]: https://github.com/cyberprophet/mint/pull/44
[#43]: https://github.com/cyberprophet/mint/pull/43
[#40]: https://github.com/cyberprophet/mint/pull/40
[#39]: https://github.com/cyberprophet/mint/pull/39
[#38]: https://github.com/cyberprophet/mint/pull/38
[#32]: https://github.com/cyberprophet/mint/pull/32
[#31]: https://github.com/cyberprophet/mint/pull/31
[#30]: https://github.com/cyberprophet/mint/pull/30
[#28]: https://github.com/cyberprophet/mint/pull/28
[#27]: https://github.com/cyberprophet/mint/pull/27
[#26]: https://github.com/cyberprophet/mint/pull/26
[#25]: https://github.com/cyberprophet/mint/pull/25
[#23]: https://github.com/cyberprophet/mint/pull/23
[#22]: https://github.com/cyberprophet/mint/pull/22
[#14]: https://github.com/cyberprophet/mint/pull/14
[#7]: https://github.com/cyberprophet/mint/pull/7
[#3]: https://github.com/cyberprophet/mint/pull/3
[#1]: https://github.com/cyberprophet/mint/pull/1
