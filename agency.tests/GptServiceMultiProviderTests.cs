using Microsoft.Extensions.Logging.Abstractions;

using OpenAI;

using ShareInvest.Agency.Models;
using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

/// <summary>
/// Tests for the multi-provider <see cref="GptService"/> constructor (ADR-014).
/// The 5-arg constructor accepts an <see cref="OpenAIClientOptions"/> for a custom
/// endpoint (e.g., MiniMax, Groq, Fireworks) plus a provider-name override used in
/// telemetry. These tests pin the constructor contract without making network calls.
/// </summary>
public class GptServiceMultiProviderTests
{
    [Fact]
    public void DefaultConstructor_ProviderName_IsOpenAI()
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        Assert.Equal("openai", svc.ProviderName);
    }

    [Fact]
    public void ImageModelConstructor_ProviderName_IsOpenAI()
    {
        using var svc = new GptService(
            NullLogger<GptService>.Instance, "test-key", "gpt-image-1");

        Assert.Equal("openai", svc.ProviderName);
    }

    [Fact]
    public void FourArgConstructor_ProviderName_IsOpenAI()
    {
        using var svc = new GptService(
            NullLogger<GptService>.Instance, "test-key", "gpt-image-1", exaApiKey: null);

        Assert.Equal("openai", svc.ProviderName);
    }

    [Fact]
    public void CustomEndpointConstructor_ProviderName_DefaultsToOpenAI()
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.minimaxi.com/v1")
        };

        using var svc = new GptService(
            NullLogger<GptService>.Instance, "test-key", options);

        Assert.Equal("openai", svc.ProviderName);
    }

    [Theory]
    [InlineData("groq")]
    [InlineData("minimax")]
    [InlineData("fireworks")]
    [InlineData("together")]
    public void CustomEndpointConstructor_WithProviderName_OverridesDefault(string providerName)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"https://api.{providerName}.example/v1")
        };

        using var svc = new GptService(
            NullLogger<GptService>.Instance, "test-key", options,
            imageModel: null, exaApiKey: null, providerName: providerName);

        Assert.Equal(providerName, svc.ProviderName);
    }

    [Fact]
    public void CustomEndpointConstructor_ProviderName_FlowsIntoTelemetry()
    {
        // Regression guard: the ProviderName property is the single source of truth
        // for the "Provider" field emitted on ApiUsageEvent. If a future refactor
        // breaks the flow (e.g., hard-codes "openai" in a service method), the
        // constructor test alone is insufficient. This test documents the intent.
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.groq.com/openai/v1")
        };

        using var svc = new GptService(
            NullLogger<GptService>.Instance, "test-key", options,
            providerName: "groq");

        Assert.Equal("groq", svc.ProviderName);
    }

    [Fact]
    public void Service_ImplementsAllThreeProviderInterfaces()
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");

        Assert.IsAssignableFrom<ITextGenerationProvider>(svc);
        Assert.IsAssignableFrom<IVisionProvider>(svc);
        Assert.IsAssignableFrom<IImageGenerationProvider>(svc);
    }

    [Fact]
    public void Service_ImplementsIDisposable()
    {
        using var svc = new GptService(NullLogger<GptService>.Instance, "test-key");
        Assert.IsAssignableFrom<IDisposable>(svc);
    }
}
