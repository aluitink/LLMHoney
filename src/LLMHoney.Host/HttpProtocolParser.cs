using System.Text;
using System.Text.RegularExpressions;

namespace LLMHoney.Host;

/// <summary>
/// HTTP protocol parser that extracts meaningful HTTP request information
/// </summary>
public sealed class HttpProtocolParser : IProtocolParser
{
    private static readonly Regex HttpRequestLineRegex = new(@"^(\w+)\s+([^\s]+)\s+HTTP/(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^([^:]+):\s*(.*)$", RegexOptions.Compiled);
    
    public ProtocolData Parse(ReadOnlySpan<byte> rawData)
    {
        try
        {
            var requestText = Encoding.UTF8.GetString(rawData);
            var lines = requestText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length == 0)
            {
                return CreateFallbackData(rawData);
            }
            
            // Try to parse the request line
            var requestLine = lines[0].Trim('\r');
            var requestMatch = HttpRequestLineRegex.Match(requestLine);
            
            if (!requestMatch.Success)
            {
                return CreateFallbackData(rawData);
            }
            
            var method = requestMatch.Groups[1].Value;
            var path = requestMatch.Groups[2].Value;
            var version = requestMatch.Groups[3].Value;
            
            // Parse headers
            var headers = new Dictionary<string, string>();
            var bodyStartIndex = -1;
            
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim('\r');
                
                if (string.IsNullOrWhiteSpace(line))
                {
                    bodyStartIndex = i + 1;
                    break;
                }
                
                var headerMatch = HeaderRegex.Match(line);
                if (headerMatch.Success)
                {
                    headers[headerMatch.Groups[1].Value.Trim()] = headerMatch.Groups[2].Value.Trim();
                }
            }
            
            // Extract body if present
            var body = string.Empty;
            if (bodyStartIndex > 0 && bodyStartIndex < lines.Length)
            {
                body = string.Join('\n', lines[bodyStartIndex..]);
            }
            
            var metadata = new Dictionary<string, object>
            {
                ["method"] = method,
                ["path"] = path,
                ["version"] = version,
                ["headers"] = headers,
                ["body"] = body,
                ["hasBody"] = !string.IsNullOrEmpty(body),
                ["headerCount"] = headers.Count,
                ["requestLine"] = requestLine
            };
            
            // Build a clean parsed representation
            var parsedContent = $"{method} {path} HTTP/{version}";
            if (headers.Count > 0)
            {
                parsedContent += $"\nHeaders: {string.Join(", ", headers.Select(h => $"{h.Key}: {h.Value}"))}";
            }
            if (!string.IsNullOrEmpty(body))
            {
                parsedContent += $"\nBody: {body}";
            }
            
            return new ProtocolData(parsedContent, metadata, true, rawData.ToArray());
        }
        catch
        {
            return CreateFallbackData(rawData);
        }
    }
    
    public string BuildPrompt(ProtocolData data, SocketConfiguration config, string remoteEndpoint, DateTimeOffset timestamp)
    {
        if (!data.IsValidProtocolData)
        {
            // Fall back to generic behavior for invalid HTTP
            return new GenericProtocolParser().BuildPrompt(data, config, remoteEndpoint, timestamp);
        }
        
        var method = data.Metadata["method"].ToString();
        var path = data.Metadata["path"].ToString();
        var headers = (Dictionary<string, string>)data.Metadata["headers"];
        var hasBody = (bool)data.Metadata["hasBody"];
        var body = data.Metadata["body"].ToString();
        var requestLine = data.Metadata["requestLine"].ToString();
        
        // Use the template system with HTTP-specific placeholders
        return config.UserPromptTemplate
            .Replace("{RemoteEndpoint}", remoteEndpoint)
            .Replace("{Transport}", $"tcp-{config.Name}")
            .Replace("{ByteCount}", data.RawData?.Length.ToString() ?? "0")
            .Replace("{Timestamp}", timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            .Replace("{HexData}", data.RawData != null ? Convert.ToHexString(data.RawData) : "")
            .Replace("{ParsedContent}", data.ParsedContent)
            .Replace("{Method}", method)
            .Replace("{Path}", path)
            .Replace("{RequestLine}", requestLine)
            .Replace("{Headers}", headers.Count > 0 ? string.Join(", ", headers.Select(h => $"{h.Key}: {h.Value}")) : "")
            .Replace("{Body}", body)
            .Replace("{HasBody}", hasBody.ToString());
    }
    
    private static ProtocolData CreateFallbackData(ReadOnlySpan<byte> rawData)
    {
        // Fall back to hex representation for invalid HTTP
        var hexData = Convert.ToHexString(rawData);
        var metadata = new Dictionary<string, object>
        {
            ["hexData"] = hexData,
            ["byteCount"] = rawData.Length,
            ["parseError"] = "Invalid HTTP format"
        };
        
        return new ProtocolData(hexData, metadata, false, rawData.ToArray());
    }
}
