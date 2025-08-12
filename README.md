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
# Show usage help
dotnet run --project LLMHoney/src/LLMHoney.Host -- --help

# Normal operation
dotnet run --project LLMHoney/src/LLMHoney.Host
```

**Note**: On first run, if no `appsettings.json` file exists, the application will extract sample configurations and exit. This allows you to review and configure the settings before running the honeypot.

## Configuration

LLMHoney uses a layered configuration system with a main configuration file and individual honeypot configurations.

### Main Configuration (`appsettings.json`)

```json
{
  "Configuration": {
    "ConfigDirectory": "./configs"
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-resource.openai.azure.com/",
    "ApiKey": "your-api-key",
    "Deployment": "your-deployment-name",
    "ModelIdOverride": null,
    "MaxOutputTokens": 4096,
    "Temperature": 0.1
  }
}
```

#### Configuration Schema

**Configuration Section:**
- `ConfigDirectory` (string, required): Directory containing honeypot configuration files (default: `"./configs"`)

**AzureOpenAI Section:**
- `Endpoint` (string, required): Azure OpenAI service endpoint URL
- `ApiKey` (string, required): API key for authentication
- `Deployment` (string, required): Model deployment name
- `ModelIdOverride` (string, optional): Override model ID instead of using deployment-based detection
- `MaxOutputTokens` (int): Maximum output tokens per response (default: 512)
- `Temperature` (float): Response randomness, 0.0-1.0 (default: 0.2)

### Honeypot Configurations

Each `.json` file in the config directory defines a honeypot instance:

```json
{
  "Name": "ssh",
  "Port": 2222,
  "BindAddress": "0.0.0.0",
  "ProtocolType": "Ssh",
  "MaxBufferSize": 8192,
  "MaxConcurrentConnections": 100,
  "SystemPrompt": "You are an SSH honeypot...",
  "UserPromptTemplate": "SSH connection from {RemoteEndpoint}...",
  "IncludeMetadata": false,
  "MaxPromptLength": 4000,
  "Enabled": true,
  "EnableConversations": true,
  "SendInitialResponse": true,
  "ConversationTimeoutMinutes": 15,
  "MaxConversationTurns": 30
}
```

#### Honeypot Configuration Schema

**Required Fields:**
- `Name` (string): Unique identifier for the honeypot
- `Port` (int, 1-65535): TCP port to listen on
- `SystemPrompt` (string): LLM system prompt defining honeypot behavior

**Network Settings:**
- `BindAddress` (string): IP address to bind to (default: `"0.0.0.0"`)
- `MaxBufferSize` (int, 1024-1048576): Maximum bytes per connection (default: 4096)
- `MaxConcurrentConnections` (int, 1-1000): Maximum simultaneous connections (default: 100)

**Protocol Settings:**
- `ProtocolType` (enum): Protocol type for specialized handling
  - Options: `Generic`, `Http`, `Ssh`, `Ftp`, `Telnet`, `Smtp` (default: `Generic`)

**LLM Prompt Settings:**
- `UserPromptTemplate` (string): Template for user prompts with placeholders:
  - `{RemoteEndpoint}`: Client IP and port
  - `{ByteCount}`: Number of bytes received
  - `{HexData}`: Hexadecimal representation of data
  - `{Timestamp}`: Connection timestamp
  - `{Transport}`: Transport protocol
  - `{Method}`: HTTP method (HTTP protocol only)
  - `{Path}`: HTTP path (HTTP protocol only)
  - `{ParsedContent}`: Parsed HTTP content (HTTP protocol only)
- `MaxPromptLength` (int): Maximum hex data length in prompts (default: 4000)
- `IncludeMetadata` (bool): Include prompts in response metadata (default: true)

**Conversation Settings:**
- `EnableConversations` (bool): Maintain sessions across multiple messages (default: true)
- `SendInitialResponse` (bool): Send greeting message on connection (default: true)
- `ConversationTimeoutMinutes` (int): Session timeout in minutes (default: 10)
- `MaxConversationTurns` (int): Maximum exchanges per conversation (default: 20)

**Control:**
- `Enabled` (bool): Whether this honeypot is active (default: true)

### Runtime Configuration Management

- **Add honeypot**: Create new `.json` file → honeypot starts automatically
- **Modify settings**: Edit existing file → honeypot restarts with new settings
- **Disable honeypot**: Set `"Enabled": false` or delete the file
- **Hot reload**: Changes are detected and applied without restarting the application

### Environment Variables

Override any configuration using double underscore notation:

```bash
export Configuration__ConfigDirectory="/etc/llmhoney/configs"
export AzureOpenAI__Temperature=0.5
export AzureOpenAI__MaxOutputTokens=2048
```
