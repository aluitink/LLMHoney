using LLMHoney.Core;
using Microsoft.Extensions.Logging;

namespace LLMHoney.Host;

/// <summary>
/// Diagnostic utilities for analyzing honeypot responses and potential issues.
/// </summary>
public class DiagnosticAnalyzer
{
    private readonly IMessageCaptureRepository _repository;
    private readonly ILogger<DiagnosticAnalyzer> _logger;

    public DiagnosticAnalyzer(IMessageCaptureRepository repository, ILogger<DiagnosticAnalyzer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes recent interactions for truncation patterns.
    /// </summary>
    /// <param name="count">Number of recent interactions to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analysis summary</returns>
    public async Task<TruncationAnalysisSummary> AnalyzeRecentInteractionsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting truncation analysis of {Count} recent interactions", count);

        var interactions = await _repository.GetRecentInteractionsAsync(count, cancellationToken);
        var summary = new TruncationAnalysisSummary();

        foreach (var interaction in interactions)
        {
            summary.TotalAnalyzed++;

            // Extract metadata for analysis
            var metadata = interaction.Response.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var responseContent = interaction.Response.Content;

            // Analyze this response
            var isTruncated = TruncationAnalyzer.AnalyzeResponse(responseContent, metadata, _logger, 4096);
            
            if (isTruncated)
            {
                summary.TruncatedCount++;
                summary.TruncatedInteractions.Add(new TruncatedInteractionInfo
                {
                    InteractionId = interaction.Id,
                    Timestamp = interaction.Timestamp,
                    ResponseLength = responseContent.Length,
                    Provider = interaction.Response.Provider,
                    LastChars = responseContent.Length > 50 ? responseContent[^50..] : responseContent,
                    Metadata = metadata
                });
            }

            // Track response length distribution
            if (responseContent.Length < 50)
                summary.VeryShortResponses++;
            else if (responseContent.Length < 200)
                summary.ShortResponses++;
            else if (responseContent.Length < 1000)
                summary.MediumResponses++;
            else
                summary.LongResponses++;
        }

        summary.TruncationRate = summary.TotalAnalyzed > 0 ? (double)summary.TruncatedCount / summary.TotalAnalyzed : 0;

        _logger.LogInformation("Truncation analysis complete - {TruncatedCount}/{TotalAnalyzed} responses may be truncated ({TruncationRate:P1})",
                              summary.TruncatedCount, summary.TotalAnalyzed, summary.TruncationRate);

        return summary;
    }
}

/// <summary>
/// Summary of truncation analysis results.
/// </summary>
public class TruncationAnalysisSummary
{
    public int TotalAnalyzed { get; set; }
    public int TruncatedCount { get; set; }
    public double TruncationRate { get; set; }
    
    public int VeryShortResponses { get; set; } // < 50 chars
    public int ShortResponses { get; set; }     // 50-200 chars
    public int MediumResponses { get; set; }    // 200-1000 chars
    public int LongResponses { get; set; }      // > 1000 chars
    
    public List<TruncatedInteractionInfo> TruncatedInteractions { get; set; } = new();
}

/// <summary>
/// Information about a potentially truncated interaction.
/// </summary>
public class TruncatedInteractionInfo
{
    public string InteractionId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public int ResponseLength { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string LastChars { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}
