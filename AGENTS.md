# AGENTS.md — mint (P7 Agency Library)

This file is automatically read by AI assistants at the start of every session working on this repository.

---

## Session Initialization (MANDATORY)

At the start of every new session, before responding to any request:

1. Sync with remote `main` branch:
   - Check `git status` and current branch
   - If on `main` with no uncommitted changes → `git pull origin main`
   - If on a feature branch or with uncommitted changes → skip and report to the user
2. Read `README.md` — understand the public NuGet surface (`ShareInvest.Agency`), the batch agents it hosts, and the versioning policy.
3. Read `CHANGELOG.md` — understand the most recent releases. The latest entry is the authoritative source for what consumers of the NuGet see.
4. Check for open issues from P0 meta repo:
   ```bash
   gh issue list --repo cyberprophet/mint --state open --search '[P0]' --json number,title,body --limit 10
   ```
   Report any before proceeding.

Only after completing the above, respond to the user's request.

---

## Project Role

**P7 = Batch AI services library**, distributed as the public NuGet package `ShareInvest.Agency` and consumed in-process by P5 (`cyberprophet/creative-server`). No separate runtime, no server, no DB.

What lives here:

- `GptService.*.cs` — partial classes, one per subagent (Blueprint/DesignHtml/Storyboard/Research/Title/Vision/ProductInfo/ReferenceLink/StudioMint).
- DTOs, validation, retry loops, structured-output tool schemas, SSRF guards.
- Thin typed wrappers over the OpenAI + Google SDKs.

What does **not** live here (see [ADR-013](https://github.com/Creative-deliverables/page-mint/blob/main/decisions/013-p7-prompt-ownership-policy.md)):

- Agent prompts (no `Prompts/` folder, no `<EmbeddedResource Include="Prompts\**" />` in `Agency.csproj`, no inline `"""..."""` prompt string literals in C#).
- Persona selection or role-slot routing (that's P5's job — this library is persona-agnostic).

---

## P7 Agent Prompt Ownership (MANDATORY)

**Rule:** Every agent prompt MD file lives in P5's `Models/Prompts/{Persona}/*.md`. P7 methods that invoke a prompt-driven model call MUST accept the prompt as a required `string` parameter, validated with `ArgumentException.ThrowIfNullOrWhiteSpace`.

This project ships publicly on nuget.org. Agent prompts are product-specific IP and must not leak via source or compiled binaries.

Examples already compliant:
- `BlueprintAsync(string systemPrompt, ...)`
- `DesignHtmlAsync(string systemPrompt, ...)`
- `StoryboardAsync(string systemPrompt, ...)`
- `ResearchAsync(string systemPrompt, ...)`
- `GenerateTitleAsync(string systemPrompt, ...)`
- `AnalyzeReferenceLinkAsync(string systemPrompt, ...)` (from 0.13.0)
- `GenerateStudioMintAsync(string basePrompt, ...)` (from 0.13.0)

Known holdout pending migration:
- `GptService.ProductInfo.DefaultProductInfoSystemPrompt` — still a C# `const string`. Do not add new holdouts.

Adding a new subagent: author MD in P5 first, add a `Lazy<string>` loader on the P5 call site, then add the P7 method with the required parameter.

---

## Cross-Project Boundary

Active stack: P0 (meta) + P5 (consumer) + P6 (client) + P7 (this) + P8 (Hermes). Legacy P1–P4 retired 2026-04-08 per ADR-008.

Do not modify other projects' code from here. For cross-project asks, file an issue in the target repo or escalate through P0.

---

## Versioning + Release

- `agency/Agency.csproj` `<Version>` is the single source of truth for NuGet publish.
- Every release gets an entry in `CHANGELOG.md` (Keep a Changelog 1.1.0 format).
- Publish command:
  ```bash
  dotnet build -c Release
  dotnet pack -c Release -o ./nupkg
  dotnet nuget push nupkg/ShareInvest.Agency.<version>.nupkg \
    --api-key <key> \
    --source https://api.nuget.org/v3/index.json
  ```
- Breaking changes → minor bump (we're pre-1.0). Signature changes that add a required parameter count as breaking.
- NuGet versions are **immutable** once pushed. Supersede, don't replace.

---

## Agent Execution Protocol

Same as the P0 execution protocol. Minimum change, maximum precision. If unsure whether a change belongs in P7 or P5, default to asking before modifying — the P7/P5 split is load-bearing and the wrong call silently leaks IP or creates drift.
