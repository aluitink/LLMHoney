using LLMHoney.Llm.Abstractions;
using System.Collections.Concurrent;

namespace LLMHoney.Core;

/// <summary>
/// Simple in-memory repository for capturing honeypot interactions.
/// </summary>
public interface IMessageCaptureRepository
{
    Task<string> CaptureAsync(ProtocolMessage message, LlmResponse response, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CapturedInteraction>> GetRecentAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CapturedInteraction>> GetRecentInteractionsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<CapturedInteraction?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<InteractionMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
}

public sealed record CapturedInteraction(
    string Id,
    ProtocolMessage Message,
    LlmResponse Response,
    DateTimeOffset CapturedAtUtc)
{
    // Add a Timestamp property that maps to CapturedAtUtc for compatibility
    public DateTimeOffset Timestamp => CapturedAtUtc;
};

public sealed record InteractionMetrics(
    int TotalInteractions,
    int TotalBytesReceived,
    DateTimeOffset? FirstInteractionUtc,
    DateTimeOffset? LastInteractionUtc,
    IReadOnlyDictionary<string, int> ByTransport,
    IReadOnlyDictionary<string, int> ByRemoteEndpoint);

public sealed class InMemoryMessageCaptureRepository : IMessageCaptureRepository
{
    private readonly ConcurrentDictionary<string, CapturedInteraction> _interactions = new();
    private readonly ConcurrentQueue<string> _chronologicalIds = new();
    private readonly int _maxRetainedInteractions;

    public InMemoryMessageCaptureRepository(int maxRetainedInteractions = 10000)
    {
        _maxRetainedInteractions = maxRetainedInteractions;
    }

    public Task<string> CaptureAsync(ProtocolMessage message, LlmResponse response, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N")[..8]; // Short ID for now
        var interaction = new CapturedInteraction(id, message, response, DateTimeOffset.UtcNow);
        
        _interactions[id] = interaction;
        _chronologicalIds.Enqueue(id);

        // Trim if we exceed max size
        while (_chronologicalIds.Count > _maxRetainedInteractions)
        {
            if (_chronologicalIds.TryDequeue(out var oldId))
            {
                _interactions.TryRemove(oldId, out _);
            }
        }

        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<CapturedInteraction>> GetRecentAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        var recent = _chronologicalIds
            .TakeLast(count)
            .Reverse()
            .Select(id => _interactions.TryGetValue(id, out var interaction) ? interaction : null)
            .Where(i => i != null)
            .Cast<CapturedInteraction>()
            .ToList();

        return Task.FromResult<IReadOnlyList<CapturedInteraction>>(recent);
    }

    public Task<IReadOnlyList<CapturedInteraction>> GetRecentInteractionsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        // This is an alias for GetRecentAsync for the diagnostic analyzer
        return GetRecentAsync(count, cancellationToken);
    }

    public Task<CapturedInteraction?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        _interactions.TryGetValue(id, out var interaction);
        return Task.FromResult(interaction);
    }

    public Task<InteractionMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var interactions = _interactions.Values.ToList();
        
        var totalBytes = interactions.Sum(i => i.Message.RawData.Length);
        var byTransport = interactions.GroupBy(i => i.Message.Transport)
            .ToDictionary(g => g.Key, g => g.Count());
        var byEndpoint = interactions.GroupBy(i => i.Message.RemoteEndpoint)
            .ToDictionary(g => g.Key, g => g.Count());

        var metrics = new InteractionMetrics(
            TotalInteractions: interactions.Count,
            TotalBytesReceived: totalBytes,
            FirstInteractionUtc: interactions.MinBy(i => i.Message.ReceivedAtUtc)?.Message.ReceivedAtUtc,
            LastInteractionUtc: interactions.MaxBy(i => i.Message.ReceivedAtUtc)?.Message.ReceivedAtUtc,
            ByTransport: byTransport,
            ByRemoteEndpoint: byEndpoint);

        return Task.FromResult(metrics);
    }
}
