using System.Net;
using System.Net.Sockets;
using LLMHoney.Core;
using LLMHoney.Host;
using LLMHoney.Llm.Abstractions;
using LLMHoney.Llm.SemanticKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Check for help arguments first
if (args.Contains("--help") || args.Contains("-h") || args.Contains("help"))
{
    ShowUsageHelp();
    return;
}

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Load JSON config if present
    builder.Configuration.AddJsonFile("appsettings.json", optional: true)
                         .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
                         .AddEnvironmentVariables();

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();

    // Configure options
    builder.Services.Configure<ConfigurationOptions>(builder.Configuration.GetSection(ConfigurationOptions.SectionName));

    // Register embedded configuration extractor
    builder.Services.AddSingleton<IEmbeddedConfigurationExtractor, EmbeddedConfigurationExtractor>();

    // Register configuration provider
    builder.Services.AddSingleton<ISocketConfigurationProvider, FileSystemSocketConfigurationProvider>();

    // Register repositories and services
    builder.Services.AddSingleton<IMessageCaptureRepository, InMemoryMessageCaptureRepository>();

    // Register LLM client abstraction & implementation via Azure OpenAI (falls back to stub if misconfigured)
    builder.Services.AddSemanticKernelAzureOpenAI(builder.Configuration);

    builder.Services.AddHostedService<MultiSocketHoneypotListener>();

    var host = builder.Build();

    // Extract default configurations on first run (only if appsettings.json doesn't exist)
    var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
    if (!File.Exists(appSettingsPath))
    {
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("No appsettings.json found - extracting sample configurations...");
        
        var extractor = host.Services.GetRequiredService<IEmbeddedConfigurationExtractor>();
        await extractor.ExtractDefaultConfigurationsAsync();
        
        logger.LogInformation("Sample configurations extracted. Please review and configure appsettings.json before running again.");
        logger.LogInformation("See appsettings.sample.json for configuration examples.");
        return;
    }

    await host.RunAsync();
}
catch (Exception ex)
{
#if DEBUG
    Console.WriteLine($"Application error: {ex}");
#else
    Console.WriteLine($"Application error: {ex.Message}");
    Console.WriteLine("Run with a debug build for detailed error information.");
#endif
    Environment.Exit(1);
}

static void ShowUsageHelp()
{
    Console.WriteLine("LLMHoney - AI-Powered Honeypot");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help     Show this help information");
    Console.WriteLine();
    Console.WriteLine("Configuration:");
    Console.WriteLine("  Create appsettings.json with your Azure OpenAI settings.");
    Console.WriteLine("  If no appsettings.json exists, sample configurations will be extracted.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run                    # Start honeypot service");
    Console.WriteLine("  dotnet run --help             # Show this help");
    Console.WriteLine();
    Console.WriteLine("For more information, see the README.md file.");
}
