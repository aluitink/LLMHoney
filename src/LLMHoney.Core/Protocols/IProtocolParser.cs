namespace LLMHoney.Core.Protocols;

/// <summary>
/// Interface for parsing protocol-specific data into human-readable format
/// </summary>
public interface IProtocolParser
{
    /// <summary>
    /// Parse raw protocol data into a structured representation
    /// </summary>
    /// <param name="rawData">The raw bytes received from the client</param>
    /// <returns>A parsed representation of the protocol data</returns>
    ProtocolData Parse(ReadOnlySpan<byte> rawData);
}
