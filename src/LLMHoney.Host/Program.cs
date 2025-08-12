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

// Minimal milestone 1 host: sets up DI, a TCP listener service, and forwards raw data to LLM client stub.

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

// Extract default configurations on first run
var extractor = host.Services.GetRequiredService<IEmbeddedConfigurationExtractor>();
await extractor.ExtractDefaultConfigurationsAsync();

await host.RunAsync();
