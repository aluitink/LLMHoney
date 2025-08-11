using System.Text;

namespace LLMHoney.Host;

/// <summary>
/// Generic protocol parser that provides the existing hex-dump behavior
/// </summary>
public sealed class GenericProtocolParser : IProtocolParser
{
    public ProtocolData Parse(ReadOnlySpan<byte> rawData)
    {
        var hexData = Convert.ToHexString(rawData);
        var textAttempt = TryDecodeAsText(rawData);
        
        var metadata = new Dictionary<string, object>
        {
            ["hexData"] = hexData,
            ["byteCount"] = rawData.Length
        };
        
        if (!string.IsNullOrEmpty(textAttempt))
        {
            metadata["possibleText"] = textAttempt;
        }
        
        return new ProtocolData(hexData, metadata, true, rawData.ToArray());
    }
    
    public string BuildPrompt(ProtocolData data, SocketConfiguration config, string remoteEndpoint, DateTimeOffset timestamp)
    {
        var hexData = data.Metadata["hexData"].ToString()!;
        var byteCount = data.Metadata["byteCount"].ToString()!;
        
        // Use the existing template system for backward compatibility
        return config.UserPromptTemplate
            .Replace("{RemoteEndpoint}", remoteEndpoint)
            .Replace("{Transport}", $"tcp-{config.Name}")
            .Replace("{ByteCount}", byteCount)
            .Replace("{Timestamp}", timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            .Replace("{HexData}", hexData);
    }
    
    private static string? TryDecodeAsText(ReadOnlySpan<byte> data)
    {
        try
        {
            var text = Encoding.UTF8.GetString(data);
            // Only return if it looks like readable text (no control chars except common ones)
            return text.All(c => char.IsControl(c) ? c is '\r' or '\n' or '\t' : char.IsAscii(c)) ? text : null;
        }
        catch
        {
            return null;
        }
    }
}
