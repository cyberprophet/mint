using System.Reflection;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Verifies that embedded resources in the Agency assembly are loadable and non-empty.
/// Note: the Prompts/ MD files were removed in 0.13.0 (ADR-013) — prompts now live in P5.
/// This fixture validates only resources that still ship in the package.
/// </summary>
public class EmbeddedResourceTests
{
    static readonly Assembly AgencyAssembly = Assembly.GetAssembly(typeof(WebTools))!;

    [Fact]
    public void Assembly_HasNoEmbeddedPromptResources()
    {
        // ADR-013: no prompt content may ship in the public NuGet.
        // Confirm the Prompts/ folder is gone from the embedded manifest.
        var promptResources = AgencyAssembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Prompts.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(promptResources);
    }
}
