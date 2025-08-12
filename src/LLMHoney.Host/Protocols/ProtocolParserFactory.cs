using LLMHoney.Core.Protocols;
using LLMHoney.Host.Configuration;

namespace LLMHoney.Host.Protocols;

/// <summary>
/// Factory for creating protocol parsers based on protocol type
/// </summary>
public static class ProtocolParserFactory
{
    /// <summary>
    /// Create a protocol parser for the specified protocol type
    /// </summary>
    /// <param name="protocolType">The protocol type to create a parser for</param>
    /// <returns>An appropriate protocol parser instance</returns>
    public static IHostProtocolParser Create(ProtocolType protocolType)
    {
        return protocolType switch
        {
            ProtocolType.Http => new HttpProtocolParser(),
            ProtocolType.Ssh => new SshProtocolParser(),
            ProtocolType.Generic => new GenericProtocolParser(),
            // For now, everything else falls back to generic
            _ => new GenericProtocolParser()
        };
    }
}
