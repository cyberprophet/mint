using Microsoft.Extensions.Logging;

using NSubstitute;

using ShareInvest.Agency.OpenAI;

namespace ShareInvest.Agency.Tests;

public class GptServiceDisposeTests
{
    static ILogger<GptService> MockLogger() => Substitute.For<ILogger<GptService>>();

    [Fact]
    public void Dispose_2ParamCtor_DoesNotThrow()
    {
        var service = new GptService(MockLogger(), "test-key");
        service.Dispose(); // Must not throw
    }

    [Fact]
    public void Dispose_3ParamCtor_DoesNotThrow()
    {
        var service = new GptService(MockLogger(), "test-key", "dall-e-3");
        service.Dispose(); // Must not throw
    }

    [Fact]
    public void Dispose_4ParamCtor_DoesNotThrow()
    {
        var service = new GptService(MockLogger(), "test-key", "dall-e-3", null);
        service.Dispose(); // Must not throw
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new GptService(MockLogger(), "test-key");
        service.Dispose();
        service.Dispose(); // WebTools.Dispose is idempotent via HttpClient
    }
}
