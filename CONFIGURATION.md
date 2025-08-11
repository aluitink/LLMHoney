# LLMHoney Configuration Guide

LLMHoney uses a dynamic configuration system where socket configurations are loaded from a directory at runtime.

## Setup

1. **Main Configuration** (`appsettings.json`):
```json
{
  "Configuration": {
    "ConfigDirectory": "./configs"
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "Deployment": "gpt-4",
    "MaxOutputTokens": 1024,
    "Temperature": 0.1
  }
}
```

2. **Socket Configurations** (files in `./configs/` directory):

Each `.json` file defines a honeypot instance:

```json
{
  "Name": "telnet",
  "Port": 2323,
  "BindAddress": "0.0.0.0",
  "MaxBufferSize": 4096,
  "MaxConcurrentConnections": 50,
  "SystemPrompt": "You are a Telnet honeypot. Simulate login prompts.",
  "UserPromptTemplate": "Connection from {RemoteEndpoint}: {HexData}",
  "IncludeMetadata": true,
  "MaxPromptLength": 3000,
  "Enabled": true
}
```

## Configuration Properties

- **Name**: Unique identifier
- **Port**: TCP port to listen on
- **BindAddress**: IP to bind to (default: "0.0.0.0")
- **MaxBufferSize**: Max bytes per connection (default: 4096)
- **MaxConcurrentConnections**: Max simultaneous connections (default: 100)
- **SystemPrompt**: LLM system prompt for this honeypot
- **UserPromptTemplate**: Template with placeholders: `{RemoteEndpoint}`, `{ByteCount}`, `{HexData}`, `{Timestamp}`, `{Transport}`
- **IncludeMetadata**: Include prompts in response metadata (default: true)
- **MaxPromptLength**: Max hex data length (default: 4000)
- **Enabled**: Whether this honeypot is active

## Runtime Management

- **Add**: Create new `.json` file → honeypot starts
- **Modify**: Edit file → honeypot restarts
- **Disable**: Set `"Enabled": false`
- **Remove**: Delete file → honeypot stops

## Environment Variables

Override settings using double underscore notation:
```bash
export Configuration__ConfigDirectory="/etc/llmhoney/configs"
export AzureOpenAI__Temperature=0.5
```
