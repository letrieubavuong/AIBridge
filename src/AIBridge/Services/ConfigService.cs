using System.IO;
using System.Text.Json;
using AIBridge.Models;

namespace AIBridge.Services;

public class ConfigService : IConfigService
{
    private readonly ILogService _logService;
    private readonly string _configDirectory;
    private readonly string _configFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigService(ILogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIBridge"
        );
        _configFilePath = Path.Combine(_configDirectory, "config.json");
    }

    public string ConfigFilePath => _configFilePath;

    public AppConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logService.LogInfo("Configuration file not found. Creating default configuration.");
                var defaultConfig = new AppConfig();
                SaveConfig(defaultConfig);
                return defaultConfig;
            }

            var json = File.ReadAllText(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config == null)
            {
                _logService.LogWarning("Configuration file was empty or corrupted. Falling back to default settings.");
                return new AppConfig();
            }

            _logService.LogInfo("Configuration loaded successfully.");
            return config;
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to load configuration file", ex);
            return new AppConfig();
        }
    }

    public async Task<AppConfig> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logService.LogInfo("Configuration file not found. Creating default configuration.");
                var defaultConfig = new AppConfig();
                await SaveConfigAsync(defaultConfig);
                return defaultConfig;
            }

            using var stream = File.OpenRead(_configFilePath);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions);
            if (config == null)
            {
                _logService.LogWarning("Configuration file was empty or corrupted. Falling back to default settings.");
                return new AppConfig();
            }

            _logService.LogInfo("Configuration loaded successfully.");
            return config;
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to load configuration file asynchronously", ex);
            return new AppConfig();
        }
    }

    public bool SaveConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        try
        {
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configFilePath, json);
            _logService.LogInfo($"Configuration saved to {_configFilePath}.");
            return true;
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to save configuration file", ex);
            return false;
        }
    }

    public async Task<bool> SaveConfigAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        try
        {
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
            _logService.LogInfo($"Configuration saved to {_configFilePath}.");
            return true;
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to save configuration file asynchronously", ex);
            return false;
        }
    }
}
