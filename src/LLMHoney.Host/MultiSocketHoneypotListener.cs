using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LLMHoney.Core;
using LLMHoney.Llm.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMHoney.Host;

/// <summary>
/// Multi-socket TCP honeypot listener that manages multiple honeypot instances based on dynamic configuration.
/// </summary>
public sealed class MultiSocketHoneypotListener : BackgroundService
{
    private readonly ILlmClient _llmClient;
    private readonly IConversationSessionManager _sessionManager;
    private readonly IMessageCaptureRepository _repository;
    private readonly ISocketConfigurationProvider _configProvider;
    private readonly ILogger<MultiSocketHoneypotListener> _logger;
    private readonly ConcurrentDictionary<string, HoneypotInstance> _instances = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public MultiSocketHoneypotListener(
        ILlmClient llmClient,
        IConversationSessionManager sessionManager,
        IMessageCaptureRepository repository,
        ISocketConfigurationProvider configProvider,
        ILogger<MultiSocketHoneypotListener> logger)
    {
        _llmClient = llmClient;
        _sessionManager = sessionManager;
        _repository = repository;
        _configProvider = configProvider;
        _logger = logger;

        // Subscribe to configuration changes
        _configProvider.ConfigurationChanged += OnConfigurationChanged;
        _configProvider.ConfigurationRemoved += OnConfigurationRemoved;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Load initial configurations
            var configs = await _configProvider.LoadConfigurationsAsync(stoppingToken);
            
            foreach (var config in configs.Where(c => c.Enabled))
            {
                await StartHoneypotInstanceAsync(config, stoppingToken);
            }

            _logger.LogInformation("Started {Count} honeypot instances", _instances.Count);

            // Keep the service running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-socket honeypot listener crashed");
        }
        finally
        {
            await StopAllInstancesAsync();
        }
    }

    private async Task StartHoneypotInstanceAsync(SocketConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            if (_instances.ContainsKey(config.Name))
            {
                _logger.LogWarning("Honeypot instance {Name} already exists, stopping old instance first", config.Name);
                await StopHoneypotInstanceAsync(config.Name);
            }

            var instance = new HoneypotInstance(config, _llmClient, _sessionManager, _repository, _logger);
            _instances[config.Name] = instance;
            
            // Start the instance in the background
            _ = Task.Run(async () => await instance.RunAsync(_shutdownCts.Token), cancellationToken);
            
            _logger.LogInformation("Started honeypot instance {Name} on {Address}:{Port}", 
                config.Name, config.BindAddress, config.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start honeypot instance {Name}", config.Name);
        }
    }

    private async Task StopHoneypotInstanceAsync(string name)
    {
        if (_instances.TryRemove(name, out var instance))
        {
            await instance.StopAsync();
            _logger.LogInformation("Stopped honeypot instance {Name}", name);
        }
    }

    private async Task StopAllInstancesAsync()
    {
        _shutdownCts.Cancel();
        
        var stopTasks = _instances.Values.Select(instance => instance.StopAsync());
        await Task.WhenAll(stopTasks);
        
        _instances.Clear();
        _logger.LogInformation("Stopped all honeypot instances");
    }

    private async void OnConfigurationChanged(object? sender, SocketConfiguration config)
    {
        _logger.LogInformation("Configuration changed for {Name}", config.Name);
        
        if (config.Enabled)
        {
            await StartHoneypotInstanceAsync(config, _shutdownCts.Token);
        }
        else
        {
            await StopHoneypotInstanceAsync(config.Name);
        }
    }

    private async void OnConfigurationRemoved(object? sender, string configName)
    {
        _logger.LogInformation("Configuration removed for {Name}", configName);
        await StopHoneypotInstanceAsync(configName);
    }

    public override void Dispose()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// Individual honeypot instance managing a single TCP listener.
/// </summary>
internal sealed class HoneypotInstance
{
    private readonly SocketConfiguration _config;
    private readonly ILlmClient _llmClient;
    private readonly IConversationSessionManager _sessionManager;
    private readonly IMessageCaptureRepository _repository;
    private readonly ILogger _logger;
    private readonly IPEndPoint _endpoint;
    private TcpListener? _listener;
    private readonly CancellationTokenSource _instanceCts = new();

    public HoneypotInstance(
        SocketConfiguration config,
        ILlmClient llmClient,
        IConversationSessionManager sessionManager,
        IMessageCaptureRepository repository,
        ILogger logger)
    {
        _config = config;
        _llmClient = llmClient;
        _sessionManager = sessionManager;
        _repository = repository;
        _logger = logger;
        _endpoint = new IPEndPoint(IPAddress.Parse(_config.BindAddress), _config.Port);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _instanceCts.Token);
        var token = combinedCts.Token;

        try
        {
            _listener = new TcpListener(_endpoint);
            _listener.Start();
            _logger.LogInformation("Honeypot {Name} listening on {Endpoint}", _config.Name, _endpoint);

            var semaphore = new SemaphoreSlim(_config.MaxConcurrentConnections, _config.MaxConcurrentConnections);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_listener.Pending())
                    {
                        await Task.Delay(100, token);
                        continue;
                    }

                    await semaphore.WaitAsync(token);
                    var client = await _listener.AcceptTcpClientAsync(token);
                    
                    // Handle client in background, releasing semaphore when done
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(client, token);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in listener loop for {Name}", _config.Name);
                    await Task.Delay(1000, token); // Brief delay before retrying
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Honeypot {Name} crashed", _config.Name);
        }
        finally
        {
            _listener?.Stop();
        }
    }

    public async Task StopAsync()
    {
        _instanceCts.Cancel();
        _listener?.Stop();
        
        // Give a moment for cleanup
        await Task.Delay(100);
        _instanceCts.Dispose();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var c = client;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        string? sessionId = null;
        
        try
        {
            using var stream = client.GetStream();
            
            // Create session if conversations are enabled
            if (_config.EnableConversations)
            {
                var connectionId = $"{_config.Name}_{remote}_{DateTimeOffset.UtcNow.Ticks}";
                sessionId = await _sessionManager.StartSessionAsync(connectionId, _config.SystemPrompt);
                _logger.LogDebug("Started conversation session {SessionId} for {Remote} on {Name}", sessionId, remote, _config.Name);
            }

            var buffer = new byte[_config.MaxBufferSize];
            var conversationTurn = 0;
            var sessionStart = DateTimeOffset.UtcNow;

            // Send initial greeting message immediately upon connection if enabled
            if (_config.SendInitialResponse)
            {
                await SendInitialResponseAsync(stream, remote, sessionId, ct);
                conversationTurn++;
            }

            // Keep conversation going if enabled, otherwise handle single message
            while (!ct.IsCancellationRequested)
            {
                // Check conversation limits
                if (_config.EnableConversations)
                {
                    if (conversationTurn >= _config.MaxConversationTurns)
                    {
                        _logger.LogDebug("Conversation reached max turns ({MaxTurns}) for {Remote} on {Name}", _config.MaxConversationTurns, remote, _config.Name);
                        break;
                    }

                    if (DateTimeOffset.UtcNow - sessionStart > TimeSpan.FromMinutes(_config.ConversationTimeoutMinutes))
                    {
                        _logger.LogDebug("Conversation timed out for {Remote} on {Name}", remote, _config.Name);
                        break;
                    }
                }

                // Set a timeout for reading
                var readTask = stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).AsTask();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), ct);
                var completedTask = await Task.WhenAny(readTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogDebug("Read timeout for {Remote} on {Name}", remote, _config.Name);
                    break;
                }

                var read = await readTask;
                if (read <= 0)
                {
                    _logger.LogDebug("Connection closed by {Remote} on {Name}", remote, _config.Name);
                    break;
                }

                var payload = buffer.AsSpan(0, read).ToArray();
                _logger.LogInformation("Honeypot {Name} received {Bytes} bytes from {Remote} (turn {Turn})", _config.Name, read, remote, conversationTurn + 1);

                // Create message with honeypot-specific context
                var msg = new ProtocolMessage(remote, $"tcp-{_config.Name}", payload, DateTimeOffset.UtcNow);
                
                // Create LLM request with session context
                var customRequest = CreateCustomLlmRequest(msg, sessionId, ct);
                var response = await _llmClient.CompleteAsync(customRequest, ct);

                // Log response details before transmission
                _logger.LogInformation("LLM Response for {Name} - Content Length: {Length} chars, Provider: {Provider}, " +
                                     "Full Content: '{Content}'",
                                     _config.Name, response.Content.Length, response.Provider, response.Content);

                // Capture the interaction
                var interactionId = await _repository.CaptureAsync(msg, response, ct);
                _logger.LogDebug("Captured interaction {InteractionId} for {Name}", interactionId, _config.Name);

                // Send response back to client
                var respBytes = System.Text.Encoding.UTF8.GetBytes(response.Content + "\n");
                _logger.LogInformation("Transmitting {ByteCount} bytes to {Remote} on {Name} (content + newline)", 
                                     respBytes.Length, remote, _config.Name);
                
                await stream.WriteAsync(respBytes, ct);
                await stream.FlushAsync(ct); // Ensure data is sent immediately
                
                _logger.LogDebug("Successfully transmitted response to {Remote} on {Name}", remote, _config.Name);

                conversationTurn++;

                // If conversations are disabled, handle only one message and exit
                if (!_config.EnableConversations)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error handling client {Remote} on {Name}", remote, _config.Name);
        }
        finally
        {
            // Clean up session
            if (sessionId != null)
            {
                await _sessionManager.EndSessionAsync(sessionId);
                _logger.LogDebug("Ended conversation session {SessionId} for {Remote} on {Name}", sessionId, remote, _config.Name);
            }
        }
    }

    private async Task SendInitialResponseAsync(NetworkStream stream, string remote, string? sessionId, CancellationToken ct)
    {
        try
        {
            // For SSH protocol, send proper SSH version string instead of LLM response
            if (_config.ProtocolType == ProtocolType.Ssh)
            {
                var sshVersionBytes = System.Text.Encoding.UTF8.GetBytes(SshProtocolParser.GetSshVersionString());
                _logger.LogInformation("Sending SSH version string to {Remote} on {Name}: {VersionString}", 
                                     remote, _config.Name, SshProtocolParser.GetSshVersionString().Trim());
                
                await stream.WriteAsync(sshVersionBytes, ct);
                await stream.FlushAsync(ct);
                
                _logger.LogInformation("Sent SSH version string to {Remote} on {Name}", remote, _config.Name);
                return;
            }

            // For other protocols, use LLM-generated initial response
            // Create a synthetic "connection established" message to prompt the LLM for an initial response
            var connectionMessage = System.Text.Encoding.UTF8.GetBytes($"CONNECTION_ESTABLISHED_FROM_{remote}");
            var msg = new ProtocolMessage(remote, $"tcp-{_config.Name}", connectionMessage, DateTimeOffset.UtcNow);
            
            // Create LLM request for initial greeting
            var customRequest = CreateInitialLlmRequest(msg, sessionId, ct);
            var response = await _llmClient.CompleteAsync(customRequest, ct);

            // Log initial response details
            _logger.LogInformation("Initial LLM Response for {Name} - Content Length: {Length} chars, Provider: {Provider}, " +
                                 "Content: '{Content}'",
                                 _config.Name, response.Content.Length, response.Provider, response.Content);

            // Capture the initial interaction
            var interactionId = await _repository.CaptureAsync(msg, response, ct);
            _logger.LogDebug("Captured initial greeting interaction {InteractionId} for {Name}", interactionId, _config.Name);

            // Send initial response to client
            var respBytes = System.Text.Encoding.UTF8.GetBytes(response.Content + "\n");
            _logger.LogInformation("Transmitting initial greeting: {ByteCount} bytes to {Remote} on {Name}", 
                                 respBytes.Length, remote, _config.Name);
            
            await stream.WriteAsync(respBytes, ct);
            await stream.FlushAsync(ct); // Ensure data is sent immediately
            
            _logger.LogInformation("Sent initial greeting to {Remote} on {Name}", remote, _config.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send initial response to {Remote} on {Name}", remote, _config.Name);
        }
    }

    private LlmRequest CreateInitialLlmRequest(ProtocolMessage message, string? sessionId, CancellationToken ct)
    {
        // Create a special prompt for the initial connection
        var userContent = $"A new connection has been established from {message.RemoteEndpoint} to the {_config.Name} honeypot service. " +
                         $"Please provide an appropriate initial greeting or banner message that would be typical for this type of service. " +
                         $"The response should be engaging to keep the attacker interested and encourage them to continue interacting.";

        // Create a modified message for the initial greeting
        var customMessage = new ProtocolMessage(
            message.RemoteEndpoint,
            message.Transport,
            message.RawData,
            message.ReceivedAtUtc,
            new Dictionary<string, object>
            {
                ["systemPrompt"] = _config.SystemPrompt,
                ["userPrompt"] = userContent,
                ["honeypotName"] = _config.Name,
                ["includeMetadata"] = _config.IncludeMetadata,
                ["isInitialGreeting"] = true
            });

        return new LlmRequest(customMessage, sessionId, ct);
    }

    private LlmRequest CreateCustomLlmRequest(ProtocolMessage message, string? sessionId, CancellationToken ct)
    {
        // Get the appropriate protocol parser
        var parser = ProtocolParserFactory.Create(_config.ProtocolType);
        
        // Parse the protocol data
        var protocolData = parser.Parse(message.RawData);
        
        // Build the prompt using the parser
        var userContent = parser.BuildPrompt(protocolData, _config, message.RemoteEndpoint, message.ReceivedAtUtc);
        
        // Note: Truncation disabled - using full content

        // Create enhanced metadata that includes both parsed and raw data
        var enhancedMetadata = new Dictionary<string, object>
        {
            ["systemPrompt"] = _config.SystemPrompt,
            ["userPrompt"] = userContent,
            ["honeypotName"] = _config.Name,
            ["includeMetadata"] = _config.IncludeMetadata,
            ["protocolType"] = _config.ProtocolType.ToString(),
            ["parsedData"] = protocolData.ParsedContent,
            ["protocolMetadata"] = protocolData.Metadata
        };

        // Create a modified message that includes the custom prompts in metadata
        var customMessage = new ProtocolMessage(
            message.RemoteEndpoint,
            message.Transport,
            message.RawData,
            message.ReceivedAtUtc,
            enhancedMetadata);

        return new LlmRequest(customMessage, sessionId, ct);
    }
}
