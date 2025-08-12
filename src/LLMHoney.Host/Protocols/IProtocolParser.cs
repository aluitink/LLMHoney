using LLMHoney.Core.Protocols;
using LLMHoney.Host.Configuration;

namespace LLMHoney.Host.Protocols;

/// <summary>
/// Host-specific protocol parser interface that extends the core interface
/// </summary>
public interface IHostProtocolParser : IProtocolParser
{
    /// <summary>
    /// Build a contextual prompt for the LLM based on parsed data
    /// </summary>
    /// <param name="data">The parsed protocol data</param>
    /// <param name="config">The socket configuration</param>
    /// <param name="remoteEndpoint">The remote endpoint information</param>
    /// <param name="timestamp">When the data was received</param>
    /// <returns>A formatted prompt string for the LLM</returns>
    string BuildPrompt(ProtocolData data, SocketConfiguration config, string remoteEndpoint, DateTimeOffset timestamp);
}
