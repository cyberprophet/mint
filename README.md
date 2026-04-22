# ShareInvest.Agency

AI agency library for PageMint — multi-provider text and image generation services.

## Packages

| Package | Description |
|---------|-------------|
| `ShareInvest.Agency` | Core interfaces and AI service implementations |

## Installation

```bash
dotnet add package ShareInvest.Agency --source https://nuget.pkg.github.com/cyberprophet/index.json
```

## Usage

```csharp
// Register in DI
services.AddSingleton(new GptService(logger, apiKey, imageModel));

// Inject and use
public class MyService(GptService gpt)
{
    public async Task GenerateAsync()
    {
        var result = await gpt.GenerateImageAsync<BinaryData>(request);
    }
}
```

## Title Generation

```csharp
var title = await gpt.GenerateTitleAsync(systemPrompt, conversationText, cancellationToken: ct);
// Returns null if generation fails or produces empty content
// Uses gpt-5-nano by default; titles are capped at 50 characters
```

Per [ADR-013](https://github.com/cyberprophet/mint), all agent system prompts are owned by the consumer (page-mint-server) and injected at the call site. This package ships no prompt content — callers must supply `systemPrompt` to every generation method.

## Releases

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.
Current release: **v0.10.0** — available on [nuget.org](https://www.nuget.org/packages/ShareInvest.Agency/).

## License

MIT
