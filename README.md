# LLMHoney

A honeypot solution that uses LLMs to generate realistic responses to network intrusions.

## Projects

- `LLMHoney.Core` – Core library with message capture and analysis utilities
- `LLMHoney.Core.Tests` – xUnit test project
- `LLMHoney.Host` – Main honeypot service with multi-socket TCP listeners
- `LLMHoney.Llm.Abstractions` – LLM client abstractions and models
- `LLMHoney.Llm.SemanticKernel` – Azure OpenAI integration via Semantic Kernel

## Build & Test

```bash
dotnet build
dotnet test
```

## Running the Honeypot

```bash
# Normal operation
dotnet run --project LLMHoney/src/LLMHoney.Host

# Run diagnostic analysis
dotnet run --project LLMHoney/src/LLMHoney.Host analyze
```

## Troubleshooting Response Truncation

If you notice responses seem to be cut off or incomplete:

1. **Run diagnostic analysis**: `dotnet run --project LLMHoney/src/LLMHoney.Host analyze`
2. **Check MaxOutputTokens setting** in `appsettings.json` - increase if needed
3. **Review logs** for truncation warnings and token usage details
4. **Monitor network transmission** for connection issues

The system includes comprehensive logging to help identify where truncation occurs:
- Input prompt construction and length
- LLM response details including token usage
- Network transmission logging
- Automated truncation pattern detection

## Configuration

Configure Azure OpenAI settings in `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "Deployment": "your-deployment-name",
    "MaxOutputTokens": 4096,
    "Temperature": 0.1
  }
}
```
