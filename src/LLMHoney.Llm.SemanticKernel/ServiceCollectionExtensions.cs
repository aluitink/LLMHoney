using LLMHoney.Llm.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace LLMHoney.Llm.SemanticKernel;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Semantic Kernel based <see cref="ILlmClient"/> using Azure OpenAI configuration from IConfiguration.
    /// </summary>
    public static IServiceCollection AddSemanticKernelAzureOpenAI(this IServiceCollection services, IConfiguration config)
    {
        var azureSection = config.GetSection(AzureOpenAiOptions.SectionName);
        services.AddOptions<AzureOpenAiOptions>()
            .Bind(azureSection)
            .ValidateDataAnnotations();
        services.PostConfigure<AzureOpenAiOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.ApiKey)) throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
            if (!Uri.TryCreate(o.Endpoint, UriKind.Absolute, out _)) throw new InvalidOperationException("AzureOpenAI:Endpoint must be a valid absolute URI");
        });

        services.AddSingleton<SemanticKernelLlmClient>(sp =>
        {
            var azureOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOpenAiOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<SemanticKernelLlmClient>>();
            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: azureOpts.Deployment,
                endpoint: azureOpts.Endpoint,
                apiKey: azureOpts.ApiKey,
                modelId: azureOpts.ModelIdOverride);
            var kernel = builder.Build();
            return new SemanticKernelLlmClient(logger, kernel, azureOpts);
        });
        
        // Register both interfaces using the same singleton instance
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<SemanticKernelLlmClient>());
        services.AddSingleton<IConversationSessionManager>(sp => sp.GetRequiredService<SemanticKernelLlmClient>());

        return services;
    }
}
