using System.Collections.Concurrent;
using LLMHoney.Core;
using LLMHoney.Llm.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace LLMHoney.Llm.SemanticKernel;

/// <summary>
/// Basic Semantic Kernel based implementation with configurable prompts and session management.
/// </summary>
public sealed class SemanticKernelLlmClient : ILlmClient, IConversationSessionManager, IDisposable
{
    private readonly ILogger<SemanticKernelLlmClient> _logger;
    private readonly Kernel _kernel;
    private readonly AzureOpenAiOptions _azureOptions;
    private readonly IChatCompletionService? _chat;
    private readonly ConcurrentDictionary<string, ChatHistory> _sessionHistories = new();
    private readonly ConcurrentDictionary<string, string> _sessionSystemPrompts = new();

    public SemanticKernelLlmClient(
        ILogger<SemanticKernelLlmClient> logger, 
        Kernel kernel, 
        AzureOpenAiOptions azureOptions)
    {
        _logger = logger;
        _kernel = kernel;
        _azureOptions = azureOptions;
        _chat = kernel.Services.GetService(typeof(IChatCompletionService)) as IChatCompletionService;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var bytes = request.Message.RawData.Length;
        var hexData = Convert.ToHexString(request.Message.RawData);
        
        _logger.LogDebug("Starting LLM completion - Input bytes: {ByteCount}, Hex length: {HexLength}, Session: {SessionId}",
                        bytes, hexData.Length, request.SessionId ?? "None");
        
        // Default values
        string systemPrompt = "You are a honeypot simulation. Provide plausible but safe protocol responses. Keep answers short.";
        string userContent;
        bool includeMetadata = true;
        string honeypotName = "default";
        int maxPromptLength = 16000;

        if (request.Message.Metadata != null)
        {
            if (request.Message.Metadata.TryGetValue("systemPrompt", out var customSystemPrompt) && 
                customSystemPrompt is string customSystem)
            {
                systemPrompt = customSystem;
            }

            if (request.Message.Metadata.TryGetValue("userPrompt", out var customUserPrompt) && 
                customUserPrompt is string customUser)
            {
                userContent = customUser;
            }
            else
            {
                // Fallback to default template
                userContent = BuildDefaultUserPrompt(request.Message, hexData, maxPromptLength);
            }

            if (request.Message.Metadata.TryGetValue("includeMetadata", out var customIncludeMeta) && 
                customIncludeMeta is bool includeMeta)
            {
                includeMetadata = includeMeta;
            }

            if (request.Message.Metadata.TryGetValue("honeypotName", out var name) && 
                name is string nameStr)
            {
                honeypotName = nameStr;
            }
        }
        else
        {
            // Use default template
            userContent = BuildDefaultUserPrompt(request.Message, hexData, maxPromptLength);
        }
        
        string content;
        var meta = new Dictionary<string, object>
        {
            ["byteCount"] = bytes,
            ["receivedAt"] = request.Message.ReceivedAtUtc,
            ["transport"] = request.Message.Transport,
            ["honeypot"] = honeypotName
        };

        if (_chat is null)
        {
            content = $"[NoChatServiceConfigured] Honeypot {honeypotName} received {bytes} bytes (hex length {hexData.Length}).";
            _logger.LogWarning("ChatCompletionService not configured; returning stub response.");
            return new LlmResponse("SemanticKernelStub", content, meta);
        }

        try
        {
            ChatHistory chatHistory;
            
            // Use session-based chat history if available
            if (!string.IsNullOrEmpty(request.SessionId) && _sessionHistories.TryGetValue(request.SessionId, out var existingHistory))
            {
                chatHistory = existingHistory;
                // Add the new user message to the existing conversation
                chatHistory.AddUserMessage(userContent);
            }
            else
            {
                // Create new chat history for this interaction
                chatHistory = new ChatHistory();
                chatHistory.AddSystemMessage(systemPrompt);
                chatHistory.AddUserMessage(userContent);
                
                // Store the history if we have a session ID
                if (!string.IsNullOrEmpty(request.SessionId))
                {
                    _sessionHistories[request.SessionId] = chatHistory;
                    _sessionSystemPrompts[request.SessionId] = systemPrompt;
                }
            }

            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = _azureOptions.Temperature,
                MaxTokens = _azureOptions.MaxOutputTokens
            };

            var result = await _chat.GetChatMessageContentAsync(chatHistory, executionSettings: settings, kernel: _kernel, cancellationToken: cancellationToken);
            content = result.Content ?? string.Empty;
            meta["model"] = result.ModelId ?? _azureOptions.Deployment;
            
            // Log response details for truncation analysis
            _logger.LogInformation("LLM Response Details - Length: {ContentLength} chars, MaxTokens: {MaxTokens}, Model: {Model}, " +
                                 "Usage Tokens: {UsageTokens}, Finish Reason: {FinishReason}", 
                                 content.Length, 
                                 _azureOptions.MaxOutputTokens,
                                 result.ModelId ?? _azureOptions.Deployment,
                                 result.Metadata?.TryGetValue("Usage", out var usage) == true ? usage : "Unknown",
                                 result.Metadata?.TryGetValue("FinishReason", out var finishReason) == true ? finishReason : "Unknown");

            // Add token usage and finish reason to response metadata
            if (result.Metadata != null)
            {
                foreach (var kvp in result.Metadata)
                {
                    meta[$"llm_{kvp.Key}"] = kvp.Value ?? string.Empty;
                }
            }

            // Analyze response for truncation using the dedicated analyzer
            var isTruncated = TruncationAnalyzer.AnalyzeResponse(content, meta, _logger, _azureOptions.MaxOutputTokens);
            if (isTruncated)
            {
                meta["possiblyTruncated"] = true;
            }
            
            // Add the assistant's response to the chat history for ongoing conversations
            if (!string.IsNullOrEmpty(request.SessionId) && _sessionHistories.ContainsKey(request.SessionId))
            {
                chatHistory.AddAssistantMessage(content);
            }
            
            if (includeMetadata)
            {
                meta["systemPrompt"] = systemPrompt;
                meta["userPrompt"] = userContent;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure OpenAI call failed; falling back to stub.");
            content = $"[Error:{ex.GetType().Name}] Stub response for {bytes} bytes on {honeypotName}.";
            meta["error"] = ex.Message;
        }

        return new LlmResponse("AzureOpenAI", content, meta);
    }

    private string BuildDefaultUserPrompt(ProtocolMessage message, string hexData, int maxPromptLength)
    {
        var originalHexLength = hexData.Length;
        
        // Truncate hex data if it exceeds max prompt length
        if (hexData.Length > maxPromptLength)
        {
            hexData = hexData[..(maxPromptLength - 3)] + "...";
            _logger.LogWarning("Hex data truncated from {OriginalLength} to {TruncatedLength} chars (max: {MaxLength})", 
                             originalHexLength, hexData.Length, maxPromptLength);
        }
        
        string defaultTemplate = "Remote: {RemoteEndpoint}\nTransport: {Transport}\nBytes: {ByteCount}\nTimestamp: {Timestamp}\nHex: {HexData}";
        
        var prompt = defaultTemplate
            .Replace("{RemoteEndpoint}", message.RemoteEndpoint)
            .Replace("{Transport}", message.Transport)
            .Replace("{ByteCount}", message.RawData.Length.ToString())
            .Replace("{Timestamp}", message.ReceivedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            .Replace("{HexData}", hexData);

        _logger.LogDebug("Built user prompt - Total length: {PromptLength} chars, Hex data length: {HexLength} chars", 
                        prompt.Length, hexData.Length);

        return prompt;
    }

    public Task<string> StartSessionAsync(string connectionId, string systemPrompt)
    {
        var sessionId = $"{connectionId}_{DateTimeOffset.UtcNow.Ticks}";
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(systemPrompt);
        
        _sessionHistories[sessionId] = chatHistory;
        _sessionSystemPrompts[sessionId] = systemPrompt;
        
        _logger.LogDebug("Started conversation session {SessionId} for connection {ConnectionId}", sessionId, connectionId);
        return Task.FromResult(sessionId);
    }

    public Task EndSessionAsync(string sessionId)
    {
        _sessionHistories.TryRemove(sessionId, out _);
        _sessionSystemPrompts.TryRemove(sessionId, out _);
        _logger.LogDebug("Ended conversation session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    public Task<bool> SessionExistsAsync(string sessionId)
    {
        return Task.FromResult(_sessionHistories.ContainsKey(sessionId));
    }

    public void Dispose()
    {
        _sessionHistories.Clear();
        _sessionSystemPrompts.Clear();
    }
}
