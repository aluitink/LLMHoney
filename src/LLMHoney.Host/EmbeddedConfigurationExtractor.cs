using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LLMHoney.Host;

public interface IEmbeddedConfigurationExtractor
{
    Task ExtractDefaultConfigurationsAsync(CancellationToken cancellationToken = default);
}

public sealed class EmbeddedConfigurationExtractor : IEmbeddedConfigurationExtractor
{
    private readonly ConfigurationOptions _options;
    private readonly ILogger<EmbeddedConfigurationExtractor> _logger;

    public EmbeddedConfigurationExtractor(
        IOptions<ConfigurationOptions> options,
        ILogger<EmbeddedConfigurationExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExtractDefaultConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Setting up default configurations for first-time usage...");
        
        var configDir = Path.GetFullPath(_options.ConfigDirectory);
        
        // Ensure config directory exists
        if (!Directory.Exists(configDir))
        {
            _logger.LogInformation("Creating configuration directory: {Directory}", configDir);
            Directory.CreateDirectory(configDir);
        }

        await ExtractEmbeddedConfigFilesAsync(configDir, cancellationToken);
        await ExtractSampleAppSettingsAsync(cancellationToken);
        
        _logger.LogInformation("Configuration setup completed successfully");
    }

    private async Task ExtractEmbeddedConfigFilesAsync(string configDir, CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".configs.") && name.EndsWith(".json"))
            .ToList();

        _logger.LogDebug("Found {Count} embedded config resources", resourceNames.Count);

        foreach (var resourceName in resourceNames)
        {
            // Extract filename from resource name (e.g., "LLMHoney.Host.configs.ssh.json" -> "ssh.json")
            var fileName = resourceName.Split('.').TakeLast(2).Aggregate((a, b) => $"{a}.{b}");
            var targetPath = Path.Combine(configDir, fileName);

            // Only extract if file doesn't exist
            if (!File.Exists(targetPath))
            {
                _logger.LogInformation("Extracting honeypot configuration: {FileName}", fileName);
                
                await using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    await using var fileStream = File.Create(targetPath);
                    await stream.CopyToAsync(fileStream, cancellationToken);
                }
            }
            else
            {
                _logger.LogDebug("Configuration file already exists, skipping: {FileName}", fileName);
            }
        }
    }

    private async Task ExtractSampleAppSettingsAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("appsettings.sample.json"));

        if (resourceName == null)
        {
            _logger.LogWarning("Sample appsettings.json not found in embedded resources");
            return;
        }

        var targetPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.sample.json");
        
        // Only extract sample file if it doesn't exist
        if (!File.Exists(targetPath))
        {
            _logger.LogInformation("Extracting sample application settings: appsettings.sample.json");
            
            await using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                await using var fileStream = File.Create(targetPath);
                await stream.CopyToAsync(fileStream, cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug("Sample configuration file already exists, skipping: appsettings.sample.json");
        }
    }
}
