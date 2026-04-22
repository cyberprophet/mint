using System.Reflection;
using System.Runtime.CompilerServices;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// ADR-013 policy guardrail. P7 ships publicly on nuget.org and must never carry
/// agent prompts as embedded resources, MD files, or inline string literals. These
/// tests run against the compiled Agency.dll manifest — they pass when the
/// binary is clean, and fail immediately if any regression (re-introducing a
/// Prompts folder, an EmbeddedResource, or a legacy .md) slips in.
/// </summary>
public class PromptOwnershipGuardrailTests
{
    static readonly Assembly AgencyAssembly = typeof(ShareInvest.Agency.OpenAI.GptService).Assembly;

    [Fact]
    public void Manifest_HasNoPromptsFolderResources()
    {
        // ADR-013: the `agency/Prompts/` folder was deleted in 0.13.0.
        var hits = AgencyAssembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Prompts.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(hits);
    }

    [Fact]
    public void Manifest_HasNoMarkdownResources()
    {
        // Broader check: no .md file of any name may ship in the NuGet.
        var hits = AgencyAssembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(hits);
    }

    [Fact]
    public void Manifest_HasNoPromptFileExtensions()
    {
        // Guard against someone adding `.prompt` / `.txt` prompt files.
        var suspect = AgencyAssembly.GetManifestResourceNames()
            .Where(n =>
                n.EndsWith(".prompt", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(suspect);
    }

    [Fact]
    public void Assembly_HasInternalsVisibleTo_AgencyTests()
    {
        // Guardrail for test-side reflection access — if someone removes the
        // InternalsVisibleTo attribute, tests that depend on internals
        // (GptServiceInternalTests, ClassifyValidationErrorsTests) will break
        // silently. Pin the intent here so the removal surfaces as a clear failure.
        var attrs = AgencyAssembly.GetCustomAttributes<InternalsVisibleToAttribute>();

        Assert.Contains(attrs, a => string.Equals(
            a.AssemblyName, "Agency.Tests", StringComparison.Ordinal));
    }
}
