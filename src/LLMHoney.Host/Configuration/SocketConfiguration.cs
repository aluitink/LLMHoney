using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using LLMHoney.Core.Protocols;

namespace LLMHoney.Host.Configuration;

public sealed record SocketConfiguration
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; }

    public string BindAddress { get; init; } = "0.0.0.0";

    [Range(1024, 1048576)] // 1KB to 1MB
    public int MaxBufferSize { get; init; } = 4096;

    [Range(1, 1000)]
    public int MaxConcurrentConnections { get; init; } = 100;

    [Required]
    public string SystemPrompt { get; init; } = string.Empty;

    public string UserPromptTemplate { get; init; } = "Remote: {RemoteEndpoint}\nTransport: {Transport}\nBytes: {ByteCount}\nTimestamp: {Timestamp}\nHex: {HexData}";

    public bool IncludeMetadata { get; init; } = true;

    public int MaxPromptLength { get; init; } = 4000;

    public bool Enabled { get; init; } = true;
    
    /// <summary>
    /// Protocol type for specialized handling behavior
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProtocolType ProtocolType { get; init; } = ProtocolType.Generic;
    
    /// <summary>
    /// Whether to maintain conversation sessions across multiple messages from the same client.
    /// </summary>
    public bool EnableConversations { get; init; } = true;
    
    /// <summary>
    /// Whether to send an initial greeting message immediately upon client connection.
    /// </summary>
    public bool SendInitialResponse { get; init; } = true;
    
    /// <summary>
    /// Maximum duration to keep a conversation session alive (in minutes).
    /// </summary>
    public int ConversationTimeoutMinutes { get; init; } = 10;
    
    /// <summary>
    /// Maximum number of message exchanges in a single conversation.
    /// </summary>
    public int MaxConversationTurns { get; init; } = 20;
}
