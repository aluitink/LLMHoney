using Microsoft.Extensions.Logging;

namespace LLMHoney.Core;

/// <summary>
/// Utility to analyze LLM responses for potential truncation issues.
/// </summary>
public static class TruncationAnalyzer
{
    /// <summary>
    /// Analyzes a response for signs of truncation and logs findings.
    /// </summary>
    /// <param name="response">The LLM response content</param>
    /// <param name="metadata">Response metadata (may contain token usage info)</param>
    /// <param name="logger">Logger for recording analysis results</param>
    /// <param name="maxTokenLimit">The configured max token limit</param>
    /// <returns>True if truncation is suspected</returns>
    public static bool AnalyzeResponse(string response, Dictionary<string, object>? metadata, ILogger logger, int maxTokenLimit)
    {
        if (string.IsNullOrEmpty(response))
        {
            logger.LogWarning("Response is null or empty - possible truncation or error");
            return true;
        }

        var issues = new List<string>();
        var isTruncated = false;

        // Check for abrupt endings
        if (!EndsWithNaturalPunctuation(response))
        {
            issues.Add("ends abruptly without punctuation");
            isTruncated = true;
        }

        // Check for incomplete sentences
        if (HasIncompleteLastSentence(response))
        {
            issues.Add("last sentence appears incomplete");
            isTruncated = true;
        }

        // Check token usage if available
        if (metadata != null)
        {
            if (HasTokenLimitReached(metadata, maxTokenLimit))
            {
                issues.Add("token limit reached");
                isTruncated = true;
            }

            if (HasLengthLimitFinishReason(metadata))
            {
                issues.Add("finished due to length limit");
                isTruncated = true;
            }
        }

        // Check for unusually short responses for complex prompts
        if (response.Length < 10)
        {
            issues.Add("response is unusually short");
            isTruncated = true;
        }

        if (isTruncated)
        {
            logger.LogWarning("Truncation detected: {Issues}. Response length: {Length} chars. Last 100 chars: '{LastChars}'",
                            string.Join(", ", issues),
                            response.Length,
                            response.Length > 100 ? response[^100..] : response);
        }
        else
        {
            logger.LogDebug("Response analysis passed - no truncation detected. Length: {Length} chars", response.Length);
        }

        return isTruncated;
    }

    private static bool EndsWithNaturalPunctuation(string response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        
        var lastChar = response.TrimEnd()[^1];
        return lastChar is '.' or '!' or '?' or ':' or '>' or '\n' or '\r';
    }

    private static bool HasIncompleteLastSentence(string response)
    {
        if (string.IsNullOrEmpty(response)) return true;
        
        var trimmed = response.Trim();
        if (trimmed.Length == 0) return true;
        
        // Check if it ends mid-word (no space before the end, and doesn't end with punctuation)
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        
        var lastWord = words[^1];
        
        // If last word is very short and doesn't end with punctuation, might be truncated
        return lastWord.Length < 3 && !EndsWithNaturalPunctuation(lastWord);
    }

    private static bool HasTokenLimitReached(Dictionary<string, object> metadata, int maxTokenLimit)
    {
        // Check various possible keys for token usage
        foreach (var key in new[] { "llm_Usage", "Usage", "llm_TokensUsed", "TokensUsed" })
        {
            if (metadata.TryGetValue(key, out var usageObj))
            {
                // Try to extract completion tokens from usage object
                if (usageObj?.ToString()?.Contains("completion") == true)
                {
                    var usage = usageObj.ToString()!;
                    // Simple heuristic: if usage string contains numbers close to max limit
                    if (ExtractNumbersFromString(usage).Any(n => n >= maxTokenLimit * 0.9))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasLengthLimitFinishReason(Dictionary<string, object> metadata)
    {
        foreach (var key in new[] { "llm_FinishReason", "FinishReason", "llm_StopReason", "StopReason" })
        {
            if (metadata.TryGetValue(key, out var reasonObj))
            {
                var reason = reasonObj?.ToString()?.ToLowerInvariant();
                if (reason != null && (reason.Contains("length") || reason.Contains("max") || reason.Contains("limit")))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<int> ExtractNumbersFromString(string input)
    {
        var numbers = new List<int>();
        var current = "";
        
        foreach (var c in input)
        {
            if (char.IsDigit(c))
            {
                current += c;
            }
            else
            {
                if (!string.IsNullOrEmpty(current) && int.TryParse(current, out var num))
                {
                    numbers.Add(num);
                }
                current = "";
            }
        }
        
        // Don't forget the last number
        if (!string.IsNullOrEmpty(current) && int.TryParse(current, out var lastNum))
        {
            numbers.Add(lastNum);
        }
        
        return numbers;
    }
}
