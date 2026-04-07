using System.Reflection;

namespace ShareInvest.Agency.Tests;

public class EmbeddedResourceTests
{
    static readonly Assembly AgencyAssembly = Assembly.GetAssembly(typeof(WebTools))!;

    [Theory]
    [InlineData("ShareInvest.Agency.Prompts.title-system.md")]
    [InlineData("ShareInvest.Agency.Prompts.visual-dna-system.md")]
    [InlineData("ShareInvest.Agency.Prompts.librarian-system.md")]
    public void AllPromptResourcesLoad(string resourceName)
    {
        using var stream = AgencyAssembly.GetManifestResourceStream(resourceName);

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, $"Resource '{resourceName}' is empty");
    }

    [Theory]
    [InlineData("ShareInvest.Agency.Prompts.title-system.md")]
    [InlineData("ShareInvest.Agency.Prompts.visual-dna-system.md")]
    [InlineData("ShareInvest.Agency.Prompts.librarian-system.md")]
    public void AllPromptResourcesAreReadableAsText(string resourceName)
    {
        using var stream = AgencyAssembly.GetManifestResourceStream(resourceName);
        using var reader = new StreamReader(stream!);

        var content = reader.ReadToEnd();

        Assert.False(string.IsNullOrWhiteSpace(content), $"Resource '{resourceName}' has no text content");
    }
}
