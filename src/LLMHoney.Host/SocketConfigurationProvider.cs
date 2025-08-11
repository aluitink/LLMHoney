using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LLMHoney.Host;

public interface ISocketConfigurationProvider
{
    Task<IEnumerable<SocketConfiguration>> LoadConfigurationsAsync(CancellationToken cancellationToken = default);
    event EventHandler<SocketConfiguration>? ConfigurationChanged;
    event EventHandler<string>? ConfigurationRemoved;
}

public sealed class FileSystemSocketConfigurationProvider : ISocketConfigurationProvider, IDisposable
{
    private readonly ConfigurationOptions _options;
    private readonly ILogger<FileSystemSocketConfigurationProvider> _logger;
    private readonly Dictionary<string, SocketConfiguration> _configurations = new();
    private readonly Dictionary<string, DateTime> _lastModified = new();
    private FileSystemWatcher? _watcher;
    private readonly object _lock = new();

    public event EventHandler<SocketConfiguration>? ConfigurationChanged;
    public event EventHandler<string>? ConfigurationRemoved;

    public FileSystemSocketConfigurationProvider(
        IOptions<ConfigurationOptions> options,
        ILogger<FileSystemSocketConfigurationProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        
        SetupFileWatcher();
    }

    public async Task<IEnumerable<SocketConfiguration>> LoadConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        var configDir = Path.GetFullPath(_options.ConfigDirectory);
        
        if (!Directory.Exists(configDir))
        {
            _logger.LogWarning("Configuration directory does not exist: {Directory}", configDir);
            return Enumerable.Empty<SocketConfiguration>();
        }

        var configFiles = Directory.GetFiles(configDir, "*.json");
        var configurations = new List<SocketConfiguration>();

        foreach (var filePath in configFiles)
        {
            try
            {
                var config = await LoadConfigurationFromFileAsync(filePath, cancellationToken);
                if (config != null)
                {
                    configurations.Add(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {FilePath}", filePath);
            }
        }

        lock (_lock)
        {
            _configurations.Clear();
            foreach (var config in configurations)
            {
                _configurations[config.Name] = config;
            }
        }

        _logger.LogInformation("Loaded {Count} socket configurations from {Directory}", configurations.Count, configDir);
        return configurations;
    }

    private async Task<SocketConfiguration?> LoadConfigurationFromFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var config = JsonSerializer.Deserialize<SocketConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        });

        if (config == null)
        {
            _logger.LogWarning("Failed to deserialize configuration from {FilePath}", filePath);
            return null;
        }

        // If name is not set in the JSON, use the filename
        if (string.IsNullOrEmpty(config.Name))
        {
            config = config with { Name = fileName };
        }

        lock (_lock)
        {
            _lastModified[filePath] = fileInfo.LastWriteTimeUtc;
        }

        _logger.LogDebug("Loaded configuration {Name} from {FilePath}", config.Name, filePath);
        return config;
    }

    private void SetupFileWatcher()
    {
        var configDir = Path.GetFullPath(_options.ConfigDirectory);
        
        if (!Directory.Exists(configDir))
        {
            _logger.LogInformation("Creating configuration directory: {Directory}", configDir);
            Directory.CreateDirectory(configDir);
        }

        _watcher = new FileSystemWatcher(configDir, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnConfigFileChanged;
        _watcher.Created += OnConfigFileChanged;
        _watcher.Deleted += OnConfigFileDeleted;
        _watcher.Renamed += OnConfigFileRenamed;

        _logger.LogInformation("Watching for configuration changes in {Directory}", configDir);
    }

    private async void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Small delay to ensure file write is complete
            await Task.Delay(100);
            
            var fileInfo = new FileInfo(e.FullPath);
            if (!fileInfo.Exists) return;

            lock (_lock)
            {
                if (_lastModified.TryGetValue(e.FullPath, out var lastMod) && 
                    Math.Abs((fileInfo.LastWriteTimeUtc - lastMod).TotalMilliseconds) < 1000)
                {
                    return; // Skip if file hasn't really changed (duplicate events)
                }
            }

            var config = await LoadConfigurationFromFileAsync(e.FullPath, CancellationToken.None);
            if (config != null)
            {
                lock (_lock)
                {
                    _configurations[config.Name] = config;
                }
                
                _logger.LogInformation("Configuration {Name} changed and reloaded", config.Name);
                ConfigurationChanged?.Invoke(this, config);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling configuration file change: {FilePath}", e.FullPath);
        }
    }

    private void OnConfigFileDeleted(object sender, FileSystemEventArgs e)
    {
        var fileName = Path.GetFileNameWithoutExtension(e.FullPath);
        
        lock (_lock)
        {
            _lastModified.Remove(e.FullPath);
            if (_configurations.Remove(fileName))
            {
                _logger.LogInformation("Configuration {Name} removed", fileName);
                ConfigurationRemoved?.Invoke(this, fileName);
            }
        }
    }

    private void OnConfigFileRenamed(object sender, RenamedEventArgs e)
    {
        OnConfigFileDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted, e.OldFullPath, e.OldName));
        OnConfigFileChanged(sender, new FileSystemEventArgs(WatcherChangeTypes.Created, e.FullPath, e.Name));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
