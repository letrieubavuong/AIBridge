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

    public ConfigService(ILogService logService, string? customConfigFilePath = null)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        if (!string.IsNullOrWhiteSpace(customConfigFilePath))
        {
            _configFilePath = customConfigFilePath;
            _configDirectory = Path.GetDirectoryName(customConfigFilePath) ?? Path.GetTempPath();
        }
        else
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIBridge"
            );
            _configFilePath = Path.Combine(_configDirectory, "config.json");
        }
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

            SanitizeConfig(config);

            _logService.LogInfo("Configuration loaded successfully.");
            return config;
        }
        catch (Exception ex)
        {
            _logService.LogError("Failed to load configuration file", ex);
            return new AppConfig();
        }
    }

    private void SanitizeConfig(AppConfig config)
    {
        if (config == null) return;

        bool modified = false;

        // Clean up test workspace leakage or non-existent workspace paths
        if (!string.IsNullOrWhiteSpace(config.WorkspacePath))
        {
            bool isTestWorkspace = config.WorkspacePath.Contains("AIBridge_McpServerTest_", StringComparison.OrdinalIgnoreCase) ||
                                  config.WorkspacePath.Contains("AIBridge_HumanGateTest_", StringComparison.OrdinalIgnoreCase) ||
                                  config.WorkspacePath.Contains("AIBridge_GitCorrelation_", StringComparison.OrdinalIgnoreCase) ||
                                  config.WorkspacePath.Contains("AIBridge_Phase08_", StringComparison.OrdinalIgnoreCase) ||
                                  config.WorkspacePath.Contains("AIBridge_PlanStore_", StringComparison.OrdinalIgnoreCase);

            if (isTestWorkspace || !Directory.Exists(config.WorkspacePath))
            {
                _logService.LogWarning($"Sanitizing invalid or temporary test workspace path '{config.WorkspacePath}'.");
                config.WorkspacePath = string.Empty;
                modified = true;
            }
        }

        // Clean up project workspaces mapping
        if (config.ProjectWorkspaces != null && config.ProjectWorkspaces.Count > 0)
        {
            var keysToRemove = config.ProjectWorkspaces
                .Where(kv => kv.Value.Contains("AIBridge_McpServerTest_", StringComparison.OrdinalIgnoreCase) ||
                             kv.Value.Contains("AIBridge_HumanGateTest_", StringComparison.OrdinalIgnoreCase) ||
                             kv.Value.Contains("AIBridge_GitCorrelation_", StringComparison.OrdinalIgnoreCase) ||
                             !Directory.Exists(kv.Value))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var k in keysToRemove)
            {
                config.ProjectWorkspaces.Remove(k);
                modified = true;
            }
        }

        if (modified)
        {
            try
            {
                SaveConfig(config);
            }
            catch { }
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
