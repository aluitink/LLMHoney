using System.Text;

namespace LLMHoney.Host;

/// <summary>
/// SSH protocol parser that handles SSH protocol specifics
/// </summary>
public sealed class SshProtocolParser : IProtocolParser
{
    private const string SSH_VERSION_STRING = "SSH-2.0-OpenSSH_8.4p1 Debian-5\r\n";
    
    public ProtocolData Parse(ReadOnlySpan<byte> rawData)
    {
        var hexData = Convert.ToHexString(rawData);
        var textAttempt = TryDecodeAsText(rawData);
        
        var metadata = new Dictionary<string, object>
        {
            ["hexData"] = hexData,
            ["byteCount"] = rawData.Length,
            ["isSshVersionString"] = false,
            ["isSshData"] = true
        };
        
        if (!string.IsNullOrEmpty(textAttempt))
        {
            metadata["possibleText"] = textAttempt;
            
            // Check if this looks like an SSH version string
            if (textAttempt.StartsWith("SSH-", StringComparison.OrdinalIgnoreCase))
            {
                metadata["isSshVersionString"] = true;
                metadata["sshVersion"] = textAttempt.Trim();
            }
        }
        
        return new ProtocolData(textAttempt ?? hexData, metadata, true, rawData.ToArray());
    }
    
    public string BuildPrompt(ProtocolData data, SocketConfiguration config, string remoteEndpoint, DateTimeOffset timestamp)
    {
        var byteCount = data.Metadata["byteCount"].ToString()!;
        var hexData = data.Metadata["hexData"].ToString()!;
        
        // For SSH version string, provide specific context
        if (data.Metadata.TryGetValue("isSshVersionString", out var isVersionString) && 
            isVersionString is true &&
            data.Metadata.TryGetValue("sshVersion", out var version))
        {
            return $"SSH client from {remoteEndpoint} at {timestamp} sent version string: {version}\n" +
                   $"Data ({byteCount} bytes): {hexData}\n" +
                   "Generate an appropriate SSH protocol response to continue the handshake:";
        }
        
        // For other SSH data, use the configured template
        return config.UserPromptTemplate
            .Replace("{RemoteEndpoint}", remoteEndpoint)
            .Replace("{Transport}", $"tcp-ssh")
            .Replace("{ByteCount}", byteCount)
            .Replace("{Timestamp}", timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            .Replace("{HexData}", hexData);
    }
    
    /// <summary>
    /// Gets the SSH version string that should be sent as an initial response
    /// </summary>
    public static string GetSshVersionString() => SSH_VERSION_STRING;
    
    private static string? TryDecodeAsText(ReadOnlySpan<byte> data)
    {
        try
        {
            var text = Encoding.UTF8.GetString(data);
            // SSH version strings and some commands are text-based
            return text.All(c => char.IsControl(c) ? c is '\r' or '\n' or '\t' : char.IsAscii(c)) ? text : null;
        }
        catch
        {
            return null;
        }
    }
}
