using System.ComponentModel.DataAnnotations;

namespace LLMHoney.Llm.Abstractions;

public sealed class AzureOpenAiOptions
{
    public const string SectionName = "AzureOpenAI";

    [Required]
    public string Endpoint { get; init; } = string.Empty; // e.g. https://my-resource.openai.azure.com

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Deployment { get; init; } = string.Empty; // model deployment name

    public string? ModelIdOverride { get; init; } // optional explicit model id instead of deployment-based detection

    public int MaxOutputTokens { get; init; } = 512;

    public float Temperature { get; init; } = 0.2f;
}
