namespace LLMHoney.Llm.Abstractions;

/// <summary>
/// Raw protocol payload received over the honeypot socket layer.
/// </summary>
public sealed record ProtocolMessage(
    string RemoteEndpoint,
    string Transport,
    byte[] RawData,
    DateTimeOffset ReceivedAtUtc,
    IReadOnlyDictionary<string, object>? Metadata = null);

/// <summary>
/// Request passed to an LLM provider. For now just wraps the raw message.
/// </summary>
public sealed record LlmRequest(
    ProtocolMessage Message, 
    string? SessionId = null,
    CancellationToken CancellationToken = default);

/// <summary>
/// Simple response from an LLM provider.
/// </summary>
public sealed record LlmResponse(
    string Provider,
    string Content,
    IReadOnlyDictionary<string, object>? Metadata = null);

/// <summary>
/// Abstraction for sending raw protocol messages to an LLM for simulated handling.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for managing conversation sessions with chat history.
/// </summary>
public interface IConversationSessionManager
{
    Task<string> StartSessionAsync(string connectionId, string systemPrompt);
    Task EndSessionAsync(string sessionId);
    Task<bool> SessionExistsAsync(string sessionId);
}
