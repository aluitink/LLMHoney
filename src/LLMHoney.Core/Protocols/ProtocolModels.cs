namespace LLMHoney.Core.Protocols;

/// <summary>
/// Protocol types for specialized honeypot behavior
/// </summary>
public enum ProtocolType
{
    /// <summary>
    /// Generic protocol handling (default behavior)
    /// </summary>
    Generic,
    
    /// <summary>
    /// HTTP protocol handling
    /// </summary>
    Http,
    
    /// <summary>
    /// SSH protocol handling
    /// </summary>
    Ssh,
    
    /// <summary>
    /// FTP protocol handling
    /// </summary>
    Ftp,
    
    /// <summary>
    /// Telnet protocol handling
    /// </summary>
    Telnet,
    
    /// <summary>
    /// SMTP protocol handling
    /// </summary>
    Smtp
}

/// <summary>
/// Represents parsed protocol data
/// </summary>
public sealed record ProtocolData(
    string ParsedContent,
    Dictionary<string, object> Metadata,
    bool IsValidProtocolData = true,
    byte[]? RawData = null);
